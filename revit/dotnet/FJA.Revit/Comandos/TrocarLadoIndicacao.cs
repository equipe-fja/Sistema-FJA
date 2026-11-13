using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Cotagem;
using Autodesk.Revit.UI.Selection;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Cotagem › Trocar lado (janela desde 2.21.0; eixos × níveis desde 2.23.0). Nos eixos e níveis selecionados
    /// (sem seleção, pede para escolher), liga a indicação (bolha/cabeça) toda de um lado, toda do outro, dos dois
    /// lados, ou troca de lado — uma escolha para os eixos e outra para os níveis. Só na vista ativa: as pontas
    /// passam para 2D (extensão da vista) antes. Ver docs/cotagem.md.
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

            // sempre os selecionados; sem seleção, pede para escolher
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
            var sel = new List<DatumNaVista>();
            foreach (var id in ids)
            {
                var d = Ler(doc.GetElement(id) as DatumPlane, v);
                if (d != null) sel.Add(d);
            }
            if (sel.Count == 0)
            {
                TaskDialog.Show(Titulo, "Os eixos e níveis selecionados não aparecem nesta vista.");
                return Result.Cancelled;
            }

            var o = OpcoesTrocarLado.Carregar();
            var j = new JanelaTrocarLado(sel, v.Name, o);
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            j.ShowDialog();
            if (!j.Confirmado) return Result.Cancelled;
            o.Gravar();

            int mudados = 0, iguais = 0;
            var ficaram = new List<string>();
            var erros = new List<string>();
            using (var t = new Transaction(doc, "Trocar lado da indicação"))
            {
                t.Start();
                foreach (var d in sel)
                {
                    var acao = d.Eixo ? j.Eixos : j.Niveis;
                    if (acao == AcaoLado.NaoMexer) continue;
                    bool a, b;
                    switch (acao)
                    {
                        case AcaoLado.LadoA: a = true; b = false; break;
                        case AcaoLado.LadoB: a = false; b = true; break;
                        case AcaoLado.DoisLados: a = b = true; break;
                        default:   // trocar: só com indicação de um lado
                            if (d.A == d.B) { ficaram.Add(d.D.Name); continue; }
                            a = !d.A; b = !d.B; break;
                    }
                    if (a == d.A && b == d.B) { iguais++; continue; }
                    try
                    {
                        // 2D nas duas pontas: a mudança vale só nesta vista
                        foreach (var ponta in new[] { DatumEnds.End0, DatumEnds.End1 })
                            if (d.D.GetDatumExtentTypeInView(ponta, v) != DatumExtentType.ViewSpecific)
                                d.D.SetDatumExtentType(ponta, v, DatumExtentType.ViewSpecific);
                        // liga antes de desligar (o Revit pode recusar ficar sem nenhuma indicação no meio do caminho)
                        if (a && !d.A) d.D.ShowBubbleInView(d.PontaA, v);
                        if (b && !d.B) d.D.ShowBubbleInView(d.PontaB, v);
                        if (!a && d.A) d.D.HideBubbleInView(d.PontaA, v);
                        if (!b && d.B) d.D.HideBubbleInView(d.PontaB, v);
                        mudados++;
                    }
                    catch (Exception ex) { erros.Add(d.D.Name + ": " + ex.Message); }
                }
                if (mudados > 0) t.Commit(); else t.RollBack();
            }

            if (ficaram.Count + erros.Count > 0 || mudados == 0)
            {
                var txt = new List<string>();
                if (iguais > 0) txt.Add(iguais + " já estava(m) como pedido.");
                if (ficaram.Count > 0) txt.Add("Trocar de lado não vale para linhas com indicação nos dois lados ou sem nenhuma (ficaram como estavam): " + string.Join(", ", ficaram));
                txt.AddRange(erros);
                TaskDialog.Show(Titulo, (mudados == 0 ? "Nada foi alterado." : mudados + " alterado(s).") + (txt.Count > 0 ? "\n\n" + string.Join("\n\n", txt) : ""));
            }
            return Result.Succeeded;
        }

        /// <summary>Orientação na tela e qual ponta fica em cima/à esquerda (A) e embaixo/à direita (B).</summary>
        static DatumNaVista Ler(DatumPlane dp, View v)
        {
            if (dp == null) return null;
            try
            {
                if (!dp.CanBeVisibleInView(v)) return null;
                if (dp is Grid g && g.IsCurved) return null;
                IList<Curve> cs;
                try { cs = dp.GetCurvesInView(DatumExtentType.ViewSpecific, v); }
                catch { cs = dp.GetCurvesInView(DatumExtentType.Model, v); }
                var c = cs?.FirstOrDefault();
                if (c == null) return null;
                XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1), dir = (p1 - p0);
                if (dir.GetLength() < 1e-9) return null;
                XYZ R = v.RightDirection, U = v.UpDirection;
                bool vertical = Math.Abs(dir.DotProduct(U)) >= Math.Abs(dir.DotProduct(R));
                // A = em cima (vertical) ou à esquerda (horizontal)
                bool zeroEhA = vertical ? p0.DotProduct(U) > p1.DotProduct(U) : p0.DotProduct(R) < p1.DotProduct(R);
                var d = new DatumNaVista
                {
                    D = dp,
                    Eixo = dp is Grid,
                    PontaA = zeroEhA ? DatumEnds.End0 : DatumEnds.End1,
                    PontaB = zeroEhA ? DatumEnds.End1 : DatumEnds.End0,
                };
                d.A = dp.IsBubbleVisibleInView(d.PontaA, v);
                d.B = dp.IsBubbleVisibleInView(d.PontaB, v);
                return d;
            }
            catch { return null; }
        }
    }
}
