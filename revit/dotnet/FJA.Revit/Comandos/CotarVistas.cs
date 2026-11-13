using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Cotagem;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Cotagem › Cotar vistas: cota a vista ativa, ou as vistas/folhas selecionadas no navegador de projeto,
    /// pelas regras da FJA (tipo da vista × escala). Só acrescenta o que falta; o modo "Refazer as minhas"
    /// apaga e refaz as cotas criadas pela ferramenta. Ver docs/cotagem.md.
    /// </summary>
    public class CotarVistas : ComandoFJA
    {
        const string Titulo = "Cotagem";

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { mensagem = "Abra um projeto (não uma família)."; return Result.Failed; }

            var vistas = Vistas(doc, uidoc);
            if (vistas.Count == 0)
            {
                TaskDialog.Show(Titulo, "Abra uma planta, corte ou elevação, ou selecione vistas ou folhas no navegador de projeto.");
                return Result.Cancelled;
            }

            var linhas = vistas.Select(v =>
            {
                var t = Regras.Sugerir(v, out string origem);
                return new LinhaVista { Vista = v, Tipo = t, Origem = origem };
            }).ToList();

            var tipos = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear && !string.IsNullOrEmpty(t.Name))
                .OrderBy(t => t.Name).ToList();
            DimensionType padrao = null;
            try { padrao = doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.LinearDimensionType)) as DimensionType; } catch { }

            var o = OpcoesCotagem.Carregar();
            var j = new JanelaCotagem(linhas, tipos, padrao, o);
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            j.ShowDialog();
            if (!j.Confirmado) return Result.Cancelled;
            o.Gravar();

            var resultados = new List<ResultadoVista>();
            using (var tg = new TransactionGroup(doc, "Cotagem FJA"))
            {
                tg.Start();
                foreach (var l in linhas.Where(x => x.Incluir))
                {
                    using (var t = new Transaction(doc, "Cotar " + l.Vista.Name))
                    {
                        var fo = t.GetFailureHandlingOptions();
                        fo.SetFailuresPreprocessor(new SemAvisos());
                        t.SetFailureHandlingOptions(fo);
                        t.Start();
                        ResultadoVista r;
                        try
                        {
                            Marca.GravarTipo(l.Vista, l.Tipo);
                            r = Motor.Cotar(doc, l.Vista, l.Tipo, j.TipoCota, o.EspacamentoMm, o.Refazer);
                            t.Commit();
                        }
                        catch (Exception ex)
                        {
                            t.RollBack();
                            r = new ResultadoVista { Vista = l.Vista.Name };
                            r.Avisos.Add("não cotada: " + ex.Message);
                        }
                        resultados.Add(r);
                    }
                }
                tg.Assimilate();
            }

            Relatorio(resultados);
            return Result.Succeeded;
        }

        /// <summary>Vistas/folhas selecionadas no navegador de projeto; sem seleção, a vista ativa.</summary>
        static List<View> Vistas(Document doc, UIDocument uidoc)
        {
            var r = new List<View>();
            var vistos = new HashSet<ElementId>();
            void Add(View v)
            {
                if (v == null || v.IsTemplate || !Aceita(v) || !vistos.Add(v.Id)) return;
                r.Add(v);
            }
            foreach (var id in uidoc.Selection.GetElementIds())
            {
                var e = doc.GetElement(id);
                if (e is ViewSheet sh) foreach (var vid in sh.GetAllPlacedViews()) Add(doc.GetElement(vid) as View);
                else if (e is View v) Add(v);
            }
            if (r.Count == 0)
            {
                var av = doc.ActiveView;
                if (av is ViewSheet sh) foreach (var vid in sh.GetAllPlacedViews()) Add(doc.GetElement(vid) as View);
                else Add(av);
            }
            return r.OrderBy(v => v.Name).ToList();
        }

        static bool Aceita(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Avisos do Revit durante a criação (ex.: referências quase paralelas) não abrem janela; o erro aparece no relatório.</summary>
        sealed class SemAvisos : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (var f in fa.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }

        static void Relatorio(List<ResultadoVista> rs)
        {
            int criadas = rs.Sum(r => r.Criadas), ja = rs.Sum(r => r.JaExistiam), rem = rs.Sum(r => r.Removidas);
            var td = new TaskDialog(Titulo)
            {
                MainInstruction = criadas == 0 ? "Nenhuma cota nova." : criadas + (criadas == 1 ? " cota criada." : " cotas criadas."),
            };
            var txt = new List<string>();
            foreach (var r in rs)
            {
                var partes = new List<string>();
                if (r.Criadas > 0) partes.Add(r.Criadas + " criada(s), " + r.Segmentos + " trecho(s)");
                if (r.JaExistiam > 0) partes.Add(r.JaExistiam + " trecho(s) já cotado(s)");
                if (r.Removidas > 0) partes.Add(r.Removidas + " refeita(s)");
                if (r.EixosEstendidos > 0) partes.Add(r.EixosEstendidos + " eixo(s) estendido(s) nesta vista para as bolhas ficarem por fora das cotas");
                partes.AddRange(r.Avisos);
                txt.Add("• " + r.Vista + ": " + (partes.Count == 0 ? "nada a cotar" : string.Join("; ", partes)));
            }
            td.MainContent = string.Join("\n", txt);
            if (ja > 0 || rem > 0)
                td.FooterText = "Trechos já cotados (à mão ou antes) não são repetidos." + (rem > 0 ? " " + rem + " cota(s) da ferramenta foram apagadas e refeitas." : "");
            td.Show();
        }
    }
}
