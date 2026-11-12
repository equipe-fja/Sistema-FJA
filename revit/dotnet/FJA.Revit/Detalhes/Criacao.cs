using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Detalhes
{
    public enum TipoFamilia { Detalhe, Anotacao }
    public enum OrigemFamilia { Centro, InferiorEsquerdo, Escolhida }

    /// <summary>Opções da ferramenta; as de uso pessoal ficam em %APPDATA%\FJA\familia-desenho.json.</summary>
    public sealed class OpcoesFamilia
    {
        public TipoFamilia Tipo = TipoFamilia.Detalhe;
        public string Nome = "";
        public OrigemFamilia Origem = OrigemFamilia.Centro;
        public XYZ PontoEscolhido;          // coordenadas da vista
        public bool Preto;
        public bool Substituir;
        public bool SalvarFora;
        public string Pasta = "";

        static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "familia-desenho.json");

        public static OpcoesFamilia Carregar()
        {
            var o = new OpcoesFamilia();
            try
            {
                if (!File.Exists(Arquivo)) return o;
                var d = Json.LerObjeto(File.ReadAllText(Arquivo));
                if (d == null) return o;
                o.Preto = d.TryGetValue("preto", out var a) && a is bool b1 && b1;
                o.Substituir = d.TryGetValue("substituir", out var b) && b is bool b2 && b2;
                o.SalvarFora = d.TryGetValue("salvarFora", out var c) && c is bool b3 && b3;
                o.Pasta = d.Txt("pasta") ?? "";
                if (d.Txt("origem") == "inferior") o.Origem = OrigemFamilia.InferiorEsquerdo;
            }
            catch { }
            return o;
        }

        public void Gravar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                var d = new Dictionary<string, object>
                {
                    ["preto"] = Preto, ["substituir"] = Substituir, ["salvarFora"] = SalvarFora, ["pasta"] = Pasta ?? "",
                    ["origem"] = Origem == OrigemFamilia.InferiorEsquerdo ? "inferior" : "centro",
                };
                File.WriteAllText(Arquivo, Json.Gravar(d));
            }
            catch { }
        }

        public XYZ PontoDeOrigem(Desenho d)
        {
            switch (Origem)
            {
                case OrigemFamilia.InferiorEsquerdo: return d.InferiorEsquerdo;
                case OrigemFamilia.Escolhida: return PontoEscolhido ?? d.Centro;
                default: return d.Centro;
            }
        }
    }

    sealed class UsarTiposDoDestino : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes;
    }

    /// <summary>
    /// Cria a família (item de detalhe ou anotação genérica) com o desenho lido. Linhas e regiões são desenhadas
    /// de novo na família (cada estilo de linha vira uma subcategoria com o mesmo nome, cor, espessura e padrão);
    /// textos e componentes são copiados da vista (levam tipo, formatação e parâmetros) e postos no lugar.
    /// </summary>
    public static class Criacao
    {
        static readonly Regex Proibidos = new Regex(@"[\\:{}\[\]|;<>?`~/*""]");

        public static string NomeValido(string nome) => Proibidos.Replace(nome ?? "", "").Trim();

        public static string Modelo(Autodesk.Revit.ApplicationServices.Application app, TipoFamilia tipo)
        {
            var achados = new List<string>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(app.FamilyTemplatePath, "*.rft", SearchOption.AllDirectories))
                {
                    string l = Texto.Norm(Path.GetFileName(f));
                    if (tipo == TipoFamilia.Anotacao)
                    {
                        if (l.Contains("GENERIC ANNOTATION") || l.Contains("ANOTACAO GENERICA")) achados.Add(f);
                    }
                    else if ((l.Contains("DETAIL ITEM") || l.Contains("ITEM DE DETALHE")) && !l.Contains("LINE BASED") && !l.Contains("BASE NA LINHA"))
                        achados.Add(f);
                }
            }
            catch { }
            return achados.OrderBy(p => Texto.Norm(p).Contains("PORTUGUESE") ? 0 : Texto.Norm(p).Contains("METRIC") || Texto.Norm(p).Contains("METRICO") ? 1 : 2)
                .ThenBy(p => p).FirstOrDefault();
        }

        static View VistaDaFamilia(Document fdoc)
        {
            var ok = new[] { ViewType.FloorPlan, ViewType.DraftingView, ViewType.Detail, ViewType.Legend, ViewType.CeilingPlan, ViewType.EngineeringPlan };
            View resto = null;
            foreach (View v in new FilteredElementCollector(fdoc).OfClass(typeof(View)))
            {
                if (v.IsTemplate) continue;
                if (ok.Contains(v.ViewType)) return v;
                if (resto == null && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser
                    && v.ViewType != ViewType.Undefined && v.ViewType != ViewType.Internal) resto = v;
            }
            return resto;
        }

        sealed class Contexto
        {
            public Document Doc, F;
            public View VF;
            public Desenho D;
            public OpcoesFamilia O;
            public Transform T;
            public Category Cat;
            public CopyPasteOptions Copia;
            public readonly Dictionary<ElementId, GraphicsStyle> Estilos = new Dictionary<ElementId, GraphicsStyle>();
            public readonly Dictionary<ElementId, ElementId> TiposRegiao = new Dictionary<ElementId, ElementId>();
            public readonly Dictionary<ElementId, ElementId> TiposTextoPreto = new Dictionary<ElementId, ElementId>();
            public readonly List<string> Avisos = new List<string>();
            public int Linhas, Regioes, Textos, Componentes, Falhas;
            GraphicsStyle invisivel;
            bool invisivelLido;

            public GraphicsStyle Invisivel()
            {
                if (invisivelLido) return invisivel;
                invisivelLido = true;
                invisivel = new FilteredElementCollector(F).OfClass(typeof(GraphicsStyle)).Cast<GraphicsStyle>()
                    .FirstOrDefault(g => { try { return g.GraphicsStyleCategory?.BuiltInCategory == BuiltInCategory.OST_InvisibleLines; } catch { return false; } });
                return invisivel;
            }
        }

        /// <summary>Cria a família, salva o .rfa e carrega no projeto. Fora de transação.
        /// sobrescrever: substitui uma família com o mesmo nome já carregada.</summary>
        public static Family Criar(Desenho d, OpcoesFamilia o, bool sobrescrever, out List<string> avisos, out string arquivo)
        {
            Document doc = d.Doc;
            var app = doc.Application;
            string tpl = Modelo(app, o.Tipo) ?? throw new Exception("modelo de família \"" +
                (o.Tipo == TipoFamilia.Anotacao ? "Anotação genérica" : "Item de detalhe") + "\" não encontrado em " + app.FamilyTemplatePath);
            string nome = NomeValido(o.Nome);
            if (nome.Length == 0) throw new Exception("nome da família vazio");

            double k = o.Tipo == TipoFamilia.Anotacao ? 1.0 / d.Escala : 1.0;
            XYZ org = o.PontoDeOrigem(d);
            var ctx = new Contexto
            {
                Doc = doc, D = d, O = o,
                T = Transform.Identity.ScaleBasis(k).Multiply(Transform.CreateTranslation(new XYZ(-org.X, -org.Y, 0))),
            };
            ctx.Copia = new CopyPasteOptions();
            ctx.Copia.SetDuplicateTypeNamesHandler(new UsarTiposDoDestino());

            Document fdoc = app.NewFamilyDocument(tpl);
            try
            {
                ctx.F = fdoc;
                ctx.VF = VistaDaFamilia(fdoc) ?? throw new Exception("o modelo de família não tem vista para desenhar");
                ctx.Cat = fdoc.OwnerFamily.FamilyCategory;
                if (o.Tipo == TipoFamilia.Anotacao) PreCarregar(ctx);
                var falhas = new ResolverFalhas();
                using (var t = ResolverFalhas.Iniciar(fdoc, "Família do desenho", falhas))
                {
                    // o modelo de família traz textos de instrução
                    foreach (var tn in new FilteredElementCollector(fdoc).OfClass(typeof(TextNote)).ToElementIds().ToList())
                        fdoc.Delete(tn);
                    foreach (var p in d.Pecas)
                    {
                        try
                        {
                            switch (p.Tipo)
                            {
                                case TipoPeca.Curva: Curva(ctx, p); break;
                                case TipoPeca.Regiao: Regiao(ctx, p); break;
                                case TipoPeca.Texto: TextoNota(ctx, p); break;
                                case TipoPeca.Componente: Componente(ctx, p); break;
                            }
                        }
                        catch (Exception ex)
                        {
                            ctx.Falhas++;
                            if (ctx.Avisos.Count(a => a.StartsWith("Não entrou")) < 8)
                                ctx.Avisos.Add("Não entrou " + Descricao(p) + ": " + ex.Message);
                        }
                    }
                    t.Commit();
                    if (falhas.Apagados > 0) ctx.Avisos.Add(falhas.Apagados + " trecho(s) muito curto(s) foram descartados pelo Revit.");
                }
                if (ctx.Linhas + ctx.Regioes + ctx.Textos + ctx.Componentes == 0) throw new Exception("nada pôde ser desenhado na família");

                string pasta = o.SalvarFora && !string.IsNullOrWhiteSpace(o.Pasta) ? o.Pasta : Path.Combine(Path.GetTempPath(), "FJA_FAMILIAS");
                Directory.CreateDirectory(pasta);
                arquivo = Path.Combine(pasta, nome + ".rfa");
                fdoc.SaveAs(arquivo, new SaveAsOptions { OverwriteExistingFile = true });
                IFamilyLoadOptions carga = sobrescrever ? (IFamilyLoadOptions)new Sobrescrever() : new NaoSobrescrever();
                fdoc.LoadFamily(doc, carga);
            }
            finally { fdoc.Close(false); }
            avisos = ctx.Avisos.Distinct().ToList();
            return Simbolos.FamiliaPorNome(doc, nome);
        }

        static string Descricao(Peca p)
        {
            switch (p.Tipo)
            {
                case TipoPeca.Curva: return "uma linha";
                case TipoPeca.Regiao: return "uma região";
                case TipoPeca.Texto: return "o texto \"" + (p.Texto ?? "").Trim().Replace("\r", " ").Replace("\n", " ") + "\"";
                default: return p.Nome ?? "um componente";
            }
        }

        // ------------------------------------------------------------ estilos de linha
        // Cada estilo vira, na família, um estilo da categoria Linhas com o MESMO NOME do estilo do projeto.
        // Ao carregar, o Revit liga os dois pelo nome: a família herda cor, espessura e padrão exatos do projeto
        // (a espessura segue a tabela de modelo, como as linhas de detalhe, e não a de anotação). É também o
        // único tipo de estilo aceito no contorno das regiões preenchidas.
        // "Tudo preto": usa um estilo preto do projeto com a mesma espessura e padrão (ex.: PENA 2) ou cria
        // "PRETO PENA n".
        static GraphicsStyle Estilo(Contexto c, EstiloLinha es)
        {
            if (es == null) return null;
            if (c.Estilos.TryGetValue(es.Id, out var ja)) return ja;
            GraphicsStyle gs;
            if (es.Invisivel) gs = c.Invisivel();
            else
            {
                Category linhasF = c.F.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                string nome = c.O.Preto && !EhPreto(es) ? NomePreto(c, es) : es.Nome;
                if (string.IsNullOrWhiteSpace(nome)) nome = "Linha";
                Category sub;
                if (linhasF.SubCategories.Contains(nome)) sub = linhasF.SubCategories.get_Item(nome);
                else
                {
                    sub = c.F.Settings.Categories.NewSubcategory(linhasF, nome);
                    // valem só se o projeto não tiver um estilo com este nome (aí vale o do projeto)
                    sub.LineColor = c.O.Preto ? new Color(0, 0, 0) : new Color(es.R, es.G, es.B);
                    try { sub.SetLineWeight(Math.Max(1, Math.Min(16, es.Peso)), GraphicsStyleType.Projection); } catch { }
                    try { sub.SetLinePatternId(Padrao(c, es), GraphicsStyleType.Projection); } catch { }
                }
                gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            c.Estilos[es.Id] = gs;
            return gs;
        }

        static bool EhPreto(EstiloLinha es) => es.R == 0 && es.G == 0 && es.B == 0;

        /// <summary>Estilo preto do projeto com a mesma espessura e padrão (prefere "PENA n"); senão "PRETO PENA n".</summary>
        static string NomePreto(Contexto c, EstiloLinha es)
        {
            try
            {
                Category linhasP = c.Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                ElementId pad = es.Tracejado ? es.Padrao : LinePatternElement.GetSolidPatternId();
                var cands = new List<Category>();
                foreach (Category s in linhasP.SubCategories)
                {
                    try
                    {
                        var cor = s.LineColor;
                        if (cor == null || cor.Red != 0 || cor.Green != 0 || cor.Blue != 0) continue;
                        if ((s.GetLineWeight(GraphicsStyleType.Projection) ?? 1) != es.Peso) continue;
                        var p = s.GetLinePatternId(GraphicsStyleType.Projection);
                        if (p == null || p == ElementId.InvalidElementId) p = LinePatternElement.GetSolidPatternId();
                        if (p != pad) continue;
                        cands.Add(s);
                    }
                    catch { }
                }
                var melhor = cands.OrderBy(s => s.Name == "PENA " + es.Peso ? 0 : s.Name.StartsWith("PENA") ? 1 : s.Name.StartsWith("<") ? 3 : 2)
                    .ThenBy(s => s.Name.Length).FirstOrDefault();
                if (melhor != null) return melhor.Name;
            }
            catch { }
            return "PRETO PENA " + es.Peso + (es.Tracejado && !string.IsNullOrEmpty(es.NomePadrao) ? " " + NomeValido(es.NomePadrao) : "");
        }

        static ElementId Padrao(Contexto c, EstiloLinha es)
        {
            if (!es.Tracejado) return LinePatternElement.GetSolidPatternId();
            string nome = NomeValido(es.NomePadrao);
            var ja = LinePatternElement.GetLinePatternElementByName(c.F, nome);
            if (ja != null) return ja.Id;
            var orig = c.Doc.GetElement(es.Padrao) as LinePatternElement;
            if (orig == null) return LinePatternElement.GetSolidPatternId();
            var lp = new LinePattern(nome);
            lp.SetSegments(orig.GetLinePattern().GetSegments());
            return LinePatternElement.Create(c.F, lp).Id;
        }

        // ------------------------------------------------------------ peças
        static void Curva(Contexto c, Peca p)
        {
            GraphicsStyle gs = null;
            try { gs = Estilo(c, p.Estilo); }
            catch (Exception ex) { c.Avisos.Add("Estilo \"" + p.Estilo?.Nome + "\" não pôde ser criado (" + ex.Message + "); a linha ficou com o estilo padrão."); }
            var dc = c.F.FamilyCreate.NewDetailCurve(c.VF, p.Curva.CreateTransformed(c.T));
            if (gs != null)
            {
                try { dc.LineStyle = gs; }
                catch (Exception ex) { c.Avisos.Add("Estilo \"" + p.Estilo?.Nome + "\" não aceito numa linha (" + ex.Message + ")."); }
            }
            c.Linhas++;
        }

        static void Regiao(Contexto c, Peca p)
        {
            if (!c.TiposRegiao.TryGetValue(p.TipoRegiao, out var tipo))
            {
                tipo = TipoRegiao(c, c.Doc.GetElement(p.TipoRegiao) as FilledRegionType);
                c.TiposRegiao[p.TipoRegiao] = tipo;
            }
            var loops = p.Loops.Select(l => CurveLoop.CreateViaTransform(l, c.T)).ToList();
            var fr = FilledRegion.Create(c.F, tipo, c.VF.Id, loops);
            if (p.Estilo != null)
            {
                var gs = Estilo(c, p.Estilo);
                if (gs != null)
                {
                    try
                    {
                        if (FilledRegion.GetValidLineStyleIdsForFilledRegion(c.F).Contains(gs.Id)) fr.SetLineStyleId(gs.Id);
                    }
                    catch { }
                }
            }
            c.Regioes++;
        }

        /// <summary>Tipo de região recriado na família (a cópia de tipos entre documentos falha): mesmo nome, padrões
        /// de frente e fundo (copiados como FillPatternElement), cores, espessura e mascaramento.</summary>
        static ElementId TipoRegiao(Contexto c, FilledRegionType orig)
        {
            if (orig == null) throw new Exception("tipo de região não encontrado");
            string nome = Desenho.NomeDe(orig) + (c.O.Preto && !orig.IsMasking ? " - PRETO" : "");
            var tipos = new FilteredElementCollector(c.F).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            var ja = tipos.FirstOrDefault(x => Desenho.NomeDe(x) == nome);
            if (ja != null) return ja.Id;
            if (tipos.Count == 0) throw new Exception("o modelo de família não tem tipo de região para duplicar");
            var nt = (FilledRegionType)tipos[0].Duplicate(nome);
            var preto = new Color(0, 0, 0);
            nt.ForegroundPatternId = Preenchimento(c, orig.ForegroundPatternId);
            nt.BackgroundPatternId = Preenchimento(c, orig.BackgroundPatternId);
            try { nt.ForegroundPatternColor = c.O.Preto && !orig.IsMasking ? preto : orig.ForegroundPatternColor; } catch { }
            try { nt.BackgroundPatternColor = c.O.Preto && !orig.IsMasking ? preto : orig.BackgroundPatternColor; } catch { }
            try { nt.IsMasking = orig.IsMasking; } catch { }
            try { nt.LineWeight = orig.LineWeight; } catch { }
            return nt.Id;
        }

        static ElementId Preenchimento(Contexto c, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return ElementId.InvalidElementId;
            var fpe = c.Doc.GetElement(id) as FillPatternElement;
            if (fpe == null) return ElementId.InvalidElementId;
            var fp = fpe.GetFillPattern();
            var ja = FillPatternElement.GetFillPatternElementByName(c.F, fp.Target, fp.Name);
            return ja != null ? ja.Id : FillPatternElement.Create(c.F, fp).Id;
        }

        /// <summary>Tipo de texto recriado na família (quando a cópia do texto falha, ex.: anotação genérica):
        /// duplica um tipo do modelo e copia os parâmetros do tipo de origem.</summary>
        static ElementId TipoTexto(Contexto c, TextNoteType orig)
        {
            string nome = Desenho.NomeDe(orig);
            var tipos = new FilteredElementCollector(c.F).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            var ja = tipos.FirstOrDefault(x => Desenho.NomeDe(x) == nome);
            if (ja != null) return ja.Id;
            if (tipos.Count == 0) throw new Exception("o modelo de família não tem tipo de texto para duplicar");
            var nt = tipos[0].Duplicate(nome);
            foreach (Parameter po in orig.Parameters)
            {
                try
                {
                    if (po.IsReadOnly || !(po.Definition is InternalDefinition idf) || idf.BuiltInParameter == BuiltInParameter.INVALID) continue;
                    var pd = nt.get_Parameter(idf.BuiltInParameter);
                    if (pd == null || pd.IsReadOnly || pd.StorageType != po.StorageType) continue;
                    switch (po.StorageType)
                    {
                        case StorageType.Double: pd.Set(po.AsDouble()); break;
                        case StorageType.Integer: pd.Set(po.AsInteger()); break;
                        case StorageType.String: pd.Set(po.AsString() ?? ""); break;
                    }
                }
                catch { }
            }
            return nt.Id;
        }

        /// <summary>Famílias das anotações aninhadas, carregadas na família nova antes da transação
        /// (na anotação genérica a cópia pela vista falha). Fora de transação.</summary>
        static void PreCarregar(Contexto c)
        {
            var feitas = new HashSet<string>();
            foreach (var p in c.D.Pecas.Where(x => x.Tipo == TipoPeca.Componente))
            {
                var fi = c.Doc.GetElement(p.Origem) as FamilyInstance;
                var fam = fi?.Symbol?.Family;
                if (fam == null || !fam.IsEditable || !feitas.Add(fam.Name)) continue;
                if (fi.Category?.BuiltInCategory != BuiltInCategory.OST_GenericAnnotation) continue;
                try
                {
                    Document ndoc = c.Doc.EditFamily(fam);
                    try { ndoc.LoadFamily(c.F, new NaoSobrescrever()); }
                    finally { ndoc.Close(false); }
                }
                catch (Exception ex) { c.Avisos.Add("Não foi possível carregar " + fam.Name + ": " + ex.Message); }
            }
        }

        /// <summary>Plano B dos componentes: põe o tipo pré-carregado, com rotação, espelhamento e parâmetros.</summary>
        static FamilyInstance Carregar(Contexto c, FamilyInstance orig)
        {
            if (orig == null) throw new Exception("componente não encontrado");
            string fam = orig.Symbol.FamilyName, tipo = orig.Symbol.Name;
            var sym = new FilteredElementCollector(c.F).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName == fam && s.Name == tipo) ?? throw new Exception("a família não pôde ser copiada nem carregada");
            if (!sym.IsActive) { sym.Activate(); c.F.Regenerate(); }
            XYZ pt = XYZ.Zero;
            var fi = c.F.FamilyCreate.NewFamilyInstance(pt, sym, c.VF);
            // parâmetros de instância com o mesmo nome
            foreach (Parameter po in orig.Parameters)
            {
                try
                {
                    if (po.IsReadOnly || po.Definition == null) continue;
                    var pd = fi.LookupParameter(po.Definition.Name);
                    if (pd == null || pd.IsReadOnly || pd.StorageType != po.StorageType) continue;
                    switch (po.StorageType)
                    {
                        case StorageType.Double: pd.Set(po.AsDouble()); break;
                        case StorageType.Integer: pd.Set(po.AsInteger()); break;
                        case StorageType.String: pd.Set(po.AsString() ?? ""); break;
                    }
                }
                catch { }
            }
            var eixo = Line.CreateBound(pt, new XYZ(0, 0, 1));
            var bx = c.D.ParaLocal.OfVector(orig.GetTransform().BasisX);
            double ang = Math.Atan2(bx.Y, bx.X);
            if (orig.Mirrored)
            {
                ElementTransformUtils.MirrorElements(c.F, new List<ElementId> { fi.Id }, Plane.CreateByNormalAndOrigin(XYZ.BasisX, pt), false);
                ang -= Math.PI;
            }
            if (Math.Abs(ang) > 1e-6) ElementTransformUtils.RotateElement(c.F, fi.Id, eixo, ang);
            return fi;
        }

        static ElementId CopiarDaVista(Contexto c, ElementId id)
        {
            var cop = ElementTransformUtils.CopyElements(c.D.Vista, new List<ElementId> { id }, c.VF, Transform.Identity, c.Copia);
            return cop.FirstOrDefault();
        }

        static void TextoNota(Contexto c, Peca p)
        {
            XYZ alvo = c.T.OfPoint(p.Ponto);
            TextNote tn = null;
            // na anotação genérica a vista da família é uma folha e a cópia pela vista falha: vai direto ao plano B
            if (c.O.Tipo == TipoFamilia.Detalhe) try
            {
                var id = CopiarDaVista(c, p.Origem);
                tn = id != null ? c.F.GetElement(id) as TextNote : null;
            }
            catch { }
            if (tn == null)
            {
                // plano B: texto com o tipo recriado (negrito/itálico só em parte do texto se perdem)
                var orig = c.Doc.GetElement(p.Origem) as TextNote;
                var opc = new TextNoteOptions(TipoTexto(c, c.Doc.GetElement(orig.GetTypeId()) as TextNoteType)) { Rotation = p.Angulo };
                try { opc.HorizontalAlignment = orig.HorizontalAlignment; } catch { }
                tn = TextNote.Create(c.F, c.VF.Id, alvo, (p.Texto ?? "").TrimEnd('\r', '\n'), opc);
                try { tn.Width = orig.Width; } catch { }
            }
            else tn.Coord = alvo;
            if (c.O.Preto)
            {
                var tipoId = tn.GetTypeId();
                if (!c.TiposTextoPreto.TryGetValue(tipoId, out var preto))
                {
                    var tt = c.F.GetElement(tipoId) as ElementType;
                    int cor = tt?.get_Parameter(BuiltInParameter.LINE_COLOR)?.AsInteger() ?? 0;
                    if (tt == null || cor == 0) preto = tipoId;
                    else
                    {
                        string n = Desenho.NomeDe(tt) + " - PRETO";
                        var ex = new FilteredElementCollector(c.F).OfClass(typeof(TextNoteType)).FirstOrDefault(x => Desenho.NomeDe(x) == n);
                        if (ex != null) preto = ex.Id;
                        else
                        {
                            var nt = tt.Duplicate(n);
                            nt.get_Parameter(BuiltInParameter.LINE_COLOR).Set(0);
                            preto = nt.Id;
                        }
                    }
                    c.TiposTextoPreto[tipoId] = preto;
                }
                if (preto != tipoId) tn.ChangeTypeId(preto);
            }
            c.Textos++;
        }

        static void Componente(Contexto c, Peca p)
        {
            var orig = c.Doc.GetElement(p.Origem) as FamilyInstance;
            bool detalheEmAnotacao = c.O.Tipo == TipoFamilia.Anotacao && orig?.Category?.BuiltInCategory == BuiltInCategory.OST_DetailComponents;
            if (detalheEmAnotacao)
                throw new Exception("componente de detalhe não pode ficar dentro de uma anotação genérica (use item de detalhe)");
            FamilyInstance fi = null;
            if (c.O.Tipo == TipoFamilia.Detalhe) try
            {
                var id = CopiarDaVista(c, p.Origem);
                fi = id != null ? c.F.GetElement(id) as FamilyInstance : null;
            }
            catch { }
            if (fi == null) fi = Carregar(c, orig);
            if (fi.Location is LocationCurve lc && p.LinhaBase != null)
                lc.Curve = p.LinhaBase.CreateTransformed(c.T);
            else if (fi.Location is LocationPoint lp && p.Ponto != null)
            {
                XYZ alvo = c.T.OfPoint(p.Ponto);
                XYZ dv = new XYZ(alvo.X - lp.Point.X, alvo.Y - lp.Point.Y, 0);
                if (dv.GetLength() > 1e-9) ElementTransformUtils.MoveElement(c.F, fi.Id, dv);
            }
            if (c.O.Preto && c.Componentes == 0)
                c.Avisos.Add("Componentes aninhados mantêm as cores das próprias famílias.");
            if (c.O.Tipo == TipoFamilia.Anotacao && c.Componentes == 0)
                c.Avisos.Add("Componentes aninhados ficam no tamanho deles; só a posição acompanha a escala.");
            c.Componentes++;
        }

        // ------------------------------------------------------------ no projeto
        /// <summary>Põe uma instância da família no ponto de origem do desenho e apaga os elementos originais.
        /// Uma transação só (Ctrl+Z desfaz tudo). Devolve a instância.</summary>
        public static Element Substituir(Desenho d, OpcoesFamilia o, FamilySymbol sym, List<string> avisos)
        {
            Document doc = d.Doc;
            Element inst;
            using (var t = new Transaction(doc, "Substituir pelo símbolo/detalhe"))
            {
                t.Start();
                if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                inst = doc.Create.NewFamilyInstance(d.ParaModelo(o.PontoDeOrigem(d)), sym, d.Vista);
                int naoApagados = 0;
                foreach (var id in d.Originais)
                {
                    try { if (doc.GetElement(id) != null) doc.Delete(id); }
                    catch { naoApagados++; }
                }
                if (naoApagados > 0) avisos.Add(naoApagados + " elemento(s) original(is) não puderam ser apagados (ex.: membro de grupo selecionado sozinho).");
                t.Commit();
            }
            return inst;
        }
    }
}
