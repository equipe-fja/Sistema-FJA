using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Planta baixa 1:75: elementos notáveis (2.27.0). Pedido da FJA (13/11/2026): levantamento feito por nuvem de pontos —
    /// cotar o que explica como o edifício foi construído, não só os vãos:
    /// 1) mudança de espessura ao longo de paredes alinhadas: cadeia na face interna, da ponta ao degrau, entre degraus e
    ///    até a outra ponta (pelas quinas — sem cota inclinada);
    /// 2) ponta livre de parede: da ponta até a primeira parede perpendicular em que ela encosta ou que a atravessa, ao
    ///    longo do corpo da parede (sem nenhuma, o comprimento da parede);
    /// 3) pilar isolado: do eixo do pilar até a face de parede mais próxima, em cada direção.
    /// </summary>
    static class Notaveis
    {
        const double TOL = Motor.TOL;
        const double F2 = 20 / 304.8;       // 2 cm
        const double DEGRAU = 10 / 304.8;   // faces alinhadas que diferem 1 cm ou mais = mudança de espessura
        const double AFAST = 450 / 304.8;   // linha da cota a 45 cm da face, para dentro do ambiente

        /// <summary>Quina (aresta vertical) de uma parede, em coordenadas da vista.</summary>
        sealed class Quina { public Reference Ref; public double X, Y; public double Em(int eixo) => eixo == 0 ? X : Y; }

        public static void Cotar(Document doc, ViewPlan v, Leitura lt, DimensionType dt, XYZ R, XYZ U, XYZ O,
                                 List<Intervalo> ex, ResultadoVista res)
        {
            if (lt.Paredes.Count == 0) return;
            var edif = Fachada.Edificio(lt.Paredes);
            var quinas = edif.ToDictionary(w => w, w => Quinas(lt, w));
            Degraus(doc, v, dt, lt, edif, quinas, R, U, O, ex, res);
            PontasLivres(doc, v, dt, lt, edif, quinas, R, U, O, ex, res);
            Pilares(doc, v, dt, lt, edif, R, U, O, ex, res);
        }

        static List<Quina> Quinas(Leitura lt, Parede w)
        {
            var r = new List<Quina>();
            foreach (var p in lt.Planos.Where(p => ReferenceEquals(p.Dono, w)))
                foreach (var a in p.Arestas)
                {
                    var q = p.Eixo == 0 ? new Quina { Ref = a.Ref, X = a.K, Y = a.C } : new Quina { Ref = a.Ref, X = a.C, Y = a.K };
                    if (!r.Any(o => Math.Abs(o.X - q.X) < TOL && Math.Abs(o.Y - q.Y) < TOL)) r.Add(q);
                }
            return r;
        }

        static Plano P(Quina q, int k) => new Plano { Ref = q.Ref, Eixo = k, Pos = q.Em(k) };

        // ------------------------------------------------------------ 1) mudanças de espessura

        static void Degraus(Document doc, View v, DimensionType dt, Leitura lt, List<Parede> edif, Dictionary<Parede, List<Quina>> quinas,
                            XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            for (int k = 0; k < 2; k++)
            {
                int c = 1 - k;
                double cmin = edif.Min(w => w.Lo[c]), cmax = edif.Max(w => w.Hi[c]);
                var ws = edif.Where(w => w.Dir == k).ToList();
                // corridas: paredes alinhadas, encostadas ponta com ponta
                int n = ws.Count; var pai = Enumerable.Range(0, n).ToArray();
                int Raiz(int x) { while (pai[x] != x) { pai[x] = pai[pai[x]]; x = pai[x]; } return x; }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        Parede a = ws[i], b = ws[j];
                        bool ponta = Math.Abs(a.Hi[k] - b.Lo[k]) <= F2 || Math.Abs(b.Hi[k] - a.Lo[k]) <= F2;
                        bool alinhadas = a.Lo[c] < b.Hi[c] - TOL && b.Lo[c] < a.Hi[c] - TOL;
                        if (ponta && alinhadas) pai[Raiz(i)] = Raiz(j);
                    }
                foreach (var g in Enumerable.Range(0, n).GroupBy(Raiz).Where(g => g.Count() >= 2))
                {
                    var run = g.Select(i => ws[i]).OrderBy(w => w.Lo[k]).ToList();
                    // um lado só: o voltado para a origem (em cima / à esquerda); se ele for a fachada, o outro
                    int sOrigem = k == 0 ? 1 : -1;
                    foreach (int s in new[] { sOrigem, -sOrigem })
                    {
                        double Face(Parede w) => w.FaceEm(s, (w.Lo[k] + w.Hi[k]) / 2);
                        // o lado de fora do edifício já tem a cadeia da fachada
                        double extremo = s > 0 ? run.Max(Face) : run.Min(Face);
                        if (s > 0 ? extremo >= cmax - F2 : extremo <= cmin + F2) continue;

                        var pts = new List<Quina>();
                        for (int i = 0; i < run.Count - 1; i++)
                        {
                            Parede a = run[i], b = run[i + 1];
                            if (Math.Abs(Face(a) - Face(b)) <= DEGRAU) continue;
                            var sal = s > 0 ? (Face(a) > Face(b) ? a : b) : (Face(a) < Face(b) ? a : b);   // a que sobressai
                            double junta = (a.Hi[k] + b.Lo[k]) / 2;
                            var q = quinas[sal].Where(o => Math.Abs(o.Em(c) - Face(sal)) <= F2)
                                               .OrderBy(o => Math.Abs(o.Em(k) - junta)).FirstOrDefault();
                            if (q != null && Math.Abs(q.Em(k) - junta) <= F2 * 3) pts.Add(q);
                        }
                        if (pts.Count == 0) continue;
                        // pontas da corrida, na mesma face
                        var ini = quinas[run[0]].Where(o => Math.Abs(o.Em(c) - Face(run[0])) <= F2).OrderBy(o => o.Em(k)).FirstOrDefault();
                        var fim = quinas[run[run.Count - 1]].Where(o => Math.Abs(o.Em(c) - Face(run[run.Count - 1])) <= F2).OrderByDescending(o => o.Em(k)).FirstOrDefault();
                        if (ini != null) pts.Add(ini);
                        if (fim != null) pts.Add(fim);
                        var ds = new List<Plano>();
                        foreach (var q in pts.OrderBy(o => o.Em(k)))
                            if (ds.Count == 0 || q.Em(k) - ds[ds.Count - 1].Pos > TOL) ds.Add(P(q, k));
                        if (ds.Count < 2) continue;
                        Cadeia(doc, v, dt, ds, k == 0, extremo + s * AFAST, "degrau", R, U, O, ex, res);
                        break;   // um lado só
                    }
                }
            }
        }

        /// <summary>Cria os trechos que ainda não estão cotados, em sequências contínuas.</summary>
        static void Cadeia(Document doc, View v, DimensionType dt, List<Plano> ds, bool hor, double linha, string regra,
                           XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            int i = 0;
            while (i < ds.Count - 1)
            {
                if (Motor.Medido(ex, hor, ds[i].Pos, ds[i + 1].Pos)) { res.JaExistiam++; i++; continue; }
                int j = i;
                while (j < ds.Count - 1 && !Motor.Medido(ex, hor, ds[j].Pos, ds[j + 1].Pos)) j++;
                var trecho = ds.GetRange(i, j - i + 1);
                if (Fachada.Criar(doc, v, dt, trecho, hor, linha, R, U, O, regra, res))
                {
                    res.Segmentos += trecho.Count - 1;
                    for (int m = 0; m < trecho.Count - 1; m++)
                        ex.Add(new Intervalo { Horizontal = hor, A = trecho[m].Pos, B = trecho[m + 1].Pos, Linha = linha });
                }
                i = j;
            }
        }

        // ------------------------------------------------------------ 2) pontas livres

        static bool Ocupado(List<Parede> ps, Parede dono, double x, double y) =>
            ps.Any(p => !ReferenceEquals(p, dono) && p.Lo[0] - TOL < x && x < p.Hi[0] + TOL && p.Lo[1] - TOL < y && y < p.Hi[1] + TOL);

        static void PontasLivres(Document doc, View v, DimensionType dt, Leitura lt, List<Parede> edif, Dictionary<Parede, List<Quina>> quinas,
                                 XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            foreach (var w in edif)
            {
                int k = w.Dir, c = 1 - k;
                if (w.Hi[k] - w.Lo[k] < 300 / 304.8) continue;
                // lado da linha: perto da origem (paredes horizontais: em cima; verticais: à esquerda)
                int s = k == 0 ? 1 : -1;
                double linha = (s > 0 ? w.Hi[c] : w.Lo[c]) + s * AFAST;
                double meioC = (w.Lo[c] + w.Hi[c]) / 2;
                foreach (int lado in new[] { -1, 1 })
                {
                    double ponta = lado > 0 ? w.Hi[k] : w.Lo[k];
                    double tk = ponta + lado * 5 / 304.8;
                    if (k == 0 ? Ocupado(edif, w, tk, meioC) : Ocupado(edif, w, meioC, tk)) continue;   // encosta em outra parede
                    var qp = quinas[w].Where(o => Math.Abs(o.Em(k) - ponta) <= F2).OrderBy(o => s * -(o.Em(c))).FirstOrDefault();
                    if (qp == null) continue;

                    // a primeira parede perpendicular, ao longo do corpo da parede, que encosta nela ou a atravessa
                    Plano alvo = null; double melhor = double.MaxValue;
                    foreach (var p in edif)
                    {
                        if (p.Dir != c || ReferenceEquals(p, w)) continue;
                        if (p.Hi[c] < w.Lo[c] - F2 || p.Lo[c] > w.Hi[c] + F2) continue;   // não toca a parede
                        if (p.Hi[k] < w.Lo[k] - TOL || p.Lo[k] > w.Hi[k] + TOL) continue; // fora do corpo da parede
                        foreach (var f in lt.Planos.Where(f => ReferenceEquals(f.Dono, p) && f.Eixo == k && f.Sinal == lado))
                        {
                            double d = lado * (ponta - f.Pos);
                            if (d > F2 && d < melhor && f.A <= w.Hi[c] + F2 && f.B >= w.Lo[c] - F2) { melhor = d; alvo = f; }
                        }
                    }
                    Plano outra;
                    if (alvo != null) outra = alvo;
                    else
                    {
                        // sem parede perpendicular: o comprimento da parede (até a outra ponta)
                        double op = lado > 0 ? w.Lo[k] : w.Hi[k];
                        var qo = quinas[w].Where(o => Math.Abs(o.Em(k) - op) <= F2).OrderBy(o => s * -(o.Em(c))).FirstOrDefault();
                        if (qo == null) continue;
                        outra = P(qo, k);
                    }
                    var a = P(qp, k);
                    var ds = new[] { a, outra }.OrderBy(x => x.Pos).ToList();
                    if (ds[1].Pos - ds[0].Pos < TOL) continue;
                    Cadeia(doc, v, dt, ds, k == 0, linha, "ponta-livre", R, U, O, ex, res);
                }
            }
        }

        // ------------------------------------------------------------ 3) pilares isolados

        static void Pilares(Document doc, View v, DimensionType dt, Leitura lt, List<Parede> edif,
                            XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            var filtro = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns });
            double x0e = edif.Min(w => w.Lo[0]), x1e = edif.Max(w => w.Hi[0]), y0e = edif.Min(w => w.Lo[1]), y1e = edif.Max(w => w.Hi[1]);
            foreach (FamilyInstance fi in new FilteredElementCollector(doc, v.Id).WherePasses(filtro).OfClass(typeof(FamilyInstance)))
            {
                try
                {
                    var bb = fi.get_BoundingBox(v);
                    if (bb == null) continue;
                    XYZ cen = (bb.Min + bb.Max) / 2;
                    double cx = cen.DotProduct(R), cy = cen.DotProduct(U);
                    if (cx < x0e || cx > x1e || cy < y0e || cy > y1e) continue;   // fora do edifício
                    double meia = Math.Max(Math.Abs((bb.Max - bb.Min).DotProduct(R)), Math.Abs((bb.Max - bb.Min).DotProduct(U))) / 2;
                    // isolado: longe de qualquer parede
                    if (edif.Any(w => w.Lo[0] - meia - F2 < cx && cx < w.Hi[0] + meia + F2 && w.Lo[1] - meia - F2 < cy && cy < w.Hi[1] + meia + F2)) continue;

                    for (int k = 0; k < 2; k++)
                    {
                        int c = 1 - k;
                        double pc = k == 0 ? cx : cy, oc = k == 0 ? cy : cx;
                        XYZ eixo = k == 0 ? R : U;
                        // referência do eixo do pilar perpendicular a esta direção
                        var tipoRef = Math.Abs(fi.HandOrientation.DotProduct(eixo)) > 0.9 ? FamilyInstanceReferenceType.CenterLeftRight
                                    : Math.Abs(fi.FacingOrientation.DotProduct(eixo)) > 0.9 ? FamilyInstanceReferenceType.CenterFrontBack
                                    : (FamilyInstanceReferenceType?)null;
                        if (tipoRef == null) continue;
                        var rp = fi.GetReferences(tipoRef.Value).FirstOrDefault();
                        if (rp == null) continue;
                        // a face de parede mais próxima nessa direção (face que cobre o pilar no outro eixo)
                        Plano alvo = null; double melhor = double.MaxValue;
                        foreach (var f in lt.Planos)
                        {
                            if (f.Eixo != k || f.Dono == null || f.A > oc || f.B < oc) continue;
                            double d = Math.Abs(f.Pos - pc);
                            if (d < melhor && Math.Sign(pc - f.Pos) == f.Sinal) { melhor = d; alvo = f; }   // face voltada para o pilar
                        }
                        if (alvo == null) continue;
                        // a quina da parede (ponto): o plano do pilar e a face de uma parede torta não são paralelos
                        var qa = alvo.ArestaPerto(oc);
                        var pw = qa != null ? new Plano { Ref = qa.Ref, Eixo = k, Pos = qa.K } : alvo;
                        var ds = new[] { new Plano { Ref = rp, Eixo = k, Pos = pc }, pw }.OrderBy(x => x.Pos).ToList();
                        Cadeia(doc, v, dt, ds, k == 0, oc, "pilar", R, U, O, ex, res);
                    }
                }
                catch { }
            }
        }
    }
}
