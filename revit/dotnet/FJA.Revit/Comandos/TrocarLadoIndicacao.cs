using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Cotagem › Trocar lado: nos eixos e níveis selecionados, desliga a indicação (bolha/cabeça) do lado em que
    /// está e liga do lado oposto, só na vista ativa. As duas pontas passam para 2D (extensão da vista) antes,
    /// para a troca não se espalhar para outras vistas. Ver docs/cotagem.md.
    /// </summary>
    public class TrocarLadoIndicacao : ComandoFJA
    {
        const string Titulo = "Trocar lado da indicação";

        sealed class FiltroDatum : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Grid || e is Level;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            View v = doc.ActiveView;
            if (v == null || v is ViewSheet || v.ViewType == ViewType.ThreeD || v.ViewType == ViewType.Schedule)
            {
                TaskDialog.Show(Titulo, "Abra uma planta, corte ou elevação onde os eixos e níveis aparecem.");
                return Result.Cancelled;
            }

            var ids = uidoc.Selection.GetElementIds().Where(i => { var e = doc.GetElement(i); return e is Grid || e is Level; }).ToList();
            if (ids.Count == 0)
            {
                try
                {
                    var refs = uidoc.Selection.PickObjects(ObjectType.Element, new FiltroDatum(),
                        "Selecione os eixos e níveis; depois clique em Concluir");
                    ids = refs.Select(r => r.ElementId).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                if (ids.Count == 0) return Result.Cancelled;
            }

            int trocados = 0;
            var ambos = new List<string>();
            var nenhum = new List<string>();
            var erros = new List<string>();
            using (var t = new Transaction(doc, "Trocar lado da indicação"))
            {
                t.Start();
                foreach (var id in ids)
                {
                    var dp = doc.GetElement(id) as DatumPlane;
                    if (dp == null) continue;
                    string nome = dp.Name;
                    try
                    {
                        if (!dp.CanBeVisibleInView(v)) { erros.Add(nome + ": não aparece nesta vista"); continue; }
                        bool b0 = dp.IsBubbleVisibleInView(DatumEnds.End0, v);
                        bool b1 = dp.IsBubbleVisibleInView(DatumEnds.End1, v);
                        if (b0 && b1) { ambos.Add(nome); continue; }
                        if (!b0 && !b1) { nenhum.Add(nome); continue; }

                        // 2D nas duas pontas: a troca vale só nesta vista
                        foreach (var ponta in new[] { DatumEnds.End0, DatumEnds.End1 })
                            if (dp.GetDatumExtentTypeInView(ponta, v) != DatumExtentType.ViewSpecific)
                                dp.SetDatumExtentType(ponta, v, DatumExtentType.ViewSpecific);

                        var de = b0 ? DatumEnds.End0 : DatumEnds.End1;
                        var para = b0 ? DatumEnds.End1 : DatumEnds.End0;
                        dp.ShowBubbleInView(para, v);
                        dp.HideBubbleInView(de, v);
                        trocados++;
                    }
                    catch (Exception ex) { erros.Add(nome + ": " + ex.Message); }
                }
                if (trocados > 0) t.Commit(); else t.RollBack();
            }

            if (ambos.Count + nenhum.Count + erros.Count > 0)
            {
                var txt = new List<string>();
                if (ambos.Count > 0) txt.Add("Com indicação dos dois lados (ficaram como estavam): " + string.Join(", ", ambos));
                if (nenhum.Count > 0) txt.Add("Sem indicação nesta vista (ficaram como estavam): " + string.Join(", ", nenhum));
                txt.AddRange(erros);
                TaskDialog.Show(Titulo, (trocados == 0 ? "Nada foi trocado." : trocados + " trocado(s).") + "\n\n" + string.Join("\n\n", txt));
            }
            return Result.Succeeded;
        }
    }
}
