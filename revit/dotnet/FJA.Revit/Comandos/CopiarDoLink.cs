using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FJA.Revit.Links;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Links › Copiar do link: copia em massa elementos de um link para o modelo, na mesma posição, por módulo
    /// (Equipamentos: peças; Encaminhamentos: redes, com Reconectar). Ver docs/revit-links.md.
    /// </summary>
    public class CopiarDoLink : ComandoFJA
    {
        sealed class FiltroLink : ISelectionFilter
        {
            readonly ElementId link; readonly Document ldoc; readonly HashSet<long> cats;
            public FiltroLink(LinkInfo l, Modulo m) { link = l.Inst.Id; ldoc = l.Doc; cats = new HashSet<long>(m.IdsCategorias().Select(Motor.Id)); }
            public bool AllowElement(Element e) => e.Id == link;
            public bool AllowReference(Reference r, XYZ p)
            {
                try
                {
                    if (r.ElementId != link) return false;
                    var e = ldoc.GetElement(r.LinkedElementId);
                    return e != null && e.Category != null && cats.Contains(Motor.Id(e.Category.Id));
                }
                catch { return false; }
            }
        }

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument) { mensagem = "Abra um modelo de projeto antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            var est = new EstadoCopiar();
            // elementos do link já selecionados (Tab) antes de abrir
            try
            {
                var porLink = new Dictionary<string, HashSet<string>>();
                foreach (var r in uidoc.Selection.GetReferences())
                {
                    if (r.LinkedElementId == ElementId.InvalidElementId) continue;
                    if (!(doc.GetElement(r.ElementId) is RevitLinkInstance li) || li.GetLinkDocument() == null) continue;
                    var e = li.GetLinkDocument().GetElement(r.LinkedElementId);
                    if (e == null) continue;
                    if (!porLink.TryGetValue(li.UniqueId, out var s)) porLink[li.UniqueId] = s = new HashSet<string>();
                    s.Add(e.UniqueId);
                }
                if (porLink.Count == 1) { est.Link = porLink.Keys.First(); est.Escolhidos = porLink.Values.First(); }
            }
            catch { }
            while (true)
            {
                var j = new JanelaCopiar(uidoc, est);
                new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
                j.ShowDialog();
                if (j.Acao != "escolher") break;
                est = j.Estado;
                var link = LinkInfo.Todos(doc).FirstOrDefault(l => l.Uid == est.Link && l.Carregado);
                var mod = Modulo.PorId(est.Modulo);
                if (link == null || mod == null) continue;
                try
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.LinkedElement, new FiltroLink(link, mod),
                        "Selecione no link os elementos a copiar (" + mod.Nome + ") e clique em Concluir");
                    var ids = refs.Select(r => r.LinkedElementId).ToList();
                    if (mod.Redes && est.Rede) ids = Motor.RedeConectada(link.Doc, ids, mod).ToList();
                    est.Escolhidos = new HashSet<string>(ids.Select(i => link.Doc.GetElement(i)).Where(e => e != null).Select(e => e.UniqueId));
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            }
            return Result.Succeeded;
        }
    }
}
