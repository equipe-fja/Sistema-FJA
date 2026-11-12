using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Links;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Links › Reconectar: refaz, entre as cópias das redes, as ligações que existem no link e liga pontas coincidentes
    /// (também com peças do próprio modelo). Com elementos selecionados, só neles; sem seleção, em todas as cópias.
    /// </summary>
    public class Reconectar : ComandoFJA
    {
        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument) { mensagem = "Abra um modelo de projeto antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            var sel = uidoc.Selection.GetElementIds().Select(i => doc.GetElement(i)).Where(e => e != null && Motor.Conectores(e) != null).ToList();
            var alvo = sel.Count > 0 ? sel : Reconexao.CopiasComConectores(doc);
            if (alvo.Count == 0)
            {
                TaskDialog.Show("Reconectar", "Não há cópias de redes neste modelo (Links › Copiar do link, módulo Encaminhamentos).");
                return Result.Cancelled;
            }
            Reconexao.Relatorio rel;
            using (var t = new Transaction(doc, "Reconectar cópias dos links"))
            {
                t.Start();
                Motor.OpcoesTransacao(t);
                rel = Reconexao.Reconectar(doc, alvo);
                t.Commit();
            }
            var td = new TaskDialog("Reconectar")
            {
                MainInstruction = (sel.Count > 0 ? sel.Count + " elemento(s) selecionado(s)" : alvo.Count + " cópia(s) de redes") + " verificada(s)",
                MainContent = rel.Resumo() + "." + (rel.Soltas.Count > 0 ? "\nOs elementos com pontas soltas ficam selecionados no modelo." : ""),
            };
            if (rel.Soltas.Count > 0)
            {
                td.ExpandedContent = string.Join("\n", rel.Soltas.Take(40).Select(s => "Elemento " + s.Elemento + " — " + s.Motivo)) + (rel.Soltas.Count > 40 ? "\n…" : "");
                try { uidoc.Selection.SetElementIds(rel.Soltas.Select(s => s.Elemento).Distinct().ToList()); } catch { }
            }
            td.Show();
            return Result.Succeeded;
        }
    }
}
