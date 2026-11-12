using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FJA.Revit.Detalhes;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Símbolos e Detalhes › Família do desenho: cria uma família de item de detalhe ou de anotação genérica com
    /// os elementos 2D selecionados na vista (linhas de detalhe, regiões, textos, componentes de detalhe e
    /// anotações; grupos de detalhe são abertos), carrega no projeto e substitui os originais ou posiciona.
    /// Ver docs/familia-do-desenho.md.
    /// </summary>
    public class FamiliaDoDesenho : ComandoFJA
    {
        const string Titulo = "Família do desenho";

        sealed class Filtro2D : ISelectionFilter
        {
            readonly View v;
            public Filtro2D(View v) { this.v = v; }
            public bool AllowElement(Element e) => Desenho.Selecionavel(e, v);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { mensagem = "Abra um projeto (não uma família)."; return Result.Failed; }
            View v = doc.ActiveView;
            if (!Desenho.VistaAceita(v))
            {
                TaskDialog.Show(Titulo, "Abra uma planta, corte, elevação, vista de desenho, detalhe, legenda ou folha com o desenho a transformar em família.");
                return Result.Cancelled;
            }

            // seleção: a atual (só o que for 2D desta vista) ou escolher agora
            var ids = uidoc.Selection.GetElementIds().Where(i => Desenho.Selecionavel(doc.GetElement(i), v)).ToList();
            int ignorados = uidoc.Selection.GetElementIds().Count - ids.Count;
            if (ids.Count == 0)
            {
                try
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.Element, new Filtro2D(v),
                        "Selecione linhas de detalhe, regiões, textos e componentes de detalhe; depois clique em Concluir");
                    ids = refs.Select(r => r.ElementId).ToList();
                    ignorados = 0;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                if (ids.Count == 0) return Result.Cancelled;
            }

            var d = Desenho.Ler(doc, v, ids);
            if (ignorados > 0) d.Avisos.Insert(0, ignorados + " elemento(s) da seleção não são 2D desta vista e ficaram de fora.");
            if (d.Vazio)
            {
                TaskDialog.Show(Titulo, "Nada na seleção pode virar família.\n\n" + string.Join("\n", d.Avisos));
                return Result.Cancelled;
            }

            var o = Opcoes.Carregar();
            o.Tipo = v.ViewType == ViewType.Legend ? TipoFamilia.Anotacao : TipoFamilia.Detalhe;
            o.Nome = d.NomeSugerido();
            o.Origem = o.Origem == OrigemFamilia.Escolhida ? OrigemFamilia.Centro : o.Origem;

            // janela; "Escolher no modelo" fecha a janela, pede o ponto e abre de novo
            while (true)
            {
                var j = new JanelaDesenho(d, o);
                new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
                j.ShowDialog();
                if (j.Resultado == Saida.Cancelar) return Result.Cancelled;
                if (j.Resultado == Saida.Criar) break;
                try
                {
                    XYZ pt = uidoc.Selection.PickPoint("Clique no ponto de inserção da família (Esc volta sem mudar)");
                    o.PontoEscolhido = d.Local(pt);
                    o.Origem = OrigemFamilia.Escolhida;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch (Exception ex)
                {
                    TaskDialog.Show(Titulo, "Não foi possível escolher o ponto nesta vista (" + ex.Message + ").\nUse o centro ou o canto do desenho.");
                }
            }
            o.Gravar();

            // família com o mesmo nome: perguntar
            bool sobrescrever = false;
            if (Simbolos.FamiliaPorNome(doc, o.Nome) != null)
            {
                string outro = NomeLivre(doc, o.Nome);
                var td = new TaskDialog(Titulo)
                {
                    MainInstruction = "Já existe uma família \"" + o.Nome + "\" no projeto.",
                    MainContent = "Substituir atualiza todas as instâncias dela no projeto.",
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Substituir a família existente");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Criar com outro nome", "\"" + outro + "\"");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var r = td.Show();
                if (r == TaskDialogResult.CommandLink1) sobrescrever = true;
                else if (r == TaskDialogResult.CommandLink2) o.Nome = outro;
                else return Result.Cancelled;
            }

            Family fam;
            List<string> avisos;
            string arquivo;
            try { fam = Criacao.Criar(d, o, sobrescrever, out avisos, out arquivo); }
            catch (Exception ex)
            {
                TaskDialog.Show(Titulo, "A família não foi criada: " + ex.Message);
                return Result.Failed;
            }
            var sym = fam != null ? Simbolos.PrimeiroTipo(doc, fam) : null;
            if (sym == null)
            {
                TaskDialog.Show(Titulo, "A família foi salva em\n" + arquivo + "\nmas não apareceu carregada no projeto.");
                return Result.Failed;
            }

            if (o.Substituir)
            {
                try
                {
                    var inst = Criacao.Substituir(d, o, sym, avisos);
                    uidoc.Selection.SetElementIds(new List<ElementId> { inst.Id });
                }
                catch (Exception ex) { avisos.Add("A família foi carregada, mas a substituição falhou: " + ex.Message); }
                Relatorio(o, arquivo, avisos, true);
            }
            else
            {
                Relatorio(o, arquivo, avisos, false);
                try { uidoc.PromptForFamilyInstancePlacement(sym); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch (Exception ex) { TaskDialog.Show(Titulo, "A família está carregada no projeto; não foi possível iniciar o posicionamento: " + ex.Message); }
            }
            return Result.Succeeded;
        }

        static string NomeLivre(Document doc, string nome)
        {
            for (int i = 2; i < 100; i++)
            {
                string n = nome + " (" + i + ")";
                if (Simbolos.FamiliaPorNome(doc, n) == null) return n;
            }
            return nome + " " + DateTime.Now.ToString("HHmmss");
        }

        /// <summary>Só aparece se houver avisos ou se o .rfa foi salvo fora (para dizer onde).</summary>
        static void Relatorio(Opcoes o, string arquivo, List<string> avisos, bool substituiu)
        {
            if (avisos.Count == 0 && !o.SalvarFora) return;
            var txt = new List<string>();
            txt.Add("Família \"" + o.Nome + "\" carregada no projeto" + (substituiu ? " e posta no lugar dos originais." : "; posicione em seguida."));
            if (o.SalvarFora) txt.Add("Arquivo: " + arquivo);
            if (avisos.Count > 0) { txt.Add(""); txt.AddRange(avisos.Select(a => "• " + a)); }
            TaskDialog.Show(Titulo, string.Join("\n", txt));
        }
    }
}
