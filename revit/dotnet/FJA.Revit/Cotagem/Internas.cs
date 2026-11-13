using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Planta baixa 1:75 (1:41 a 1:149): cadeias internas (2.24.0). Decisões da FJA (13/11/2026): o mínimo de cadeias
    /// que meça todas as paredes, em cada direção; face acabada a face acabada, com a espessura de cada parede como
    /// trecho próprio (vão + espessura), de fachada a fachada.
    /// </summary>
    static class Internas
    {
        const double TOL = Motor.TOL;
        const double MARGEM = 100 / 304.8;   // a linha fica a pelo menos 10 cm de qualquer ponta de face
        const int MAX_LINHAS = 8;            // por direção

        public static void Cotar(Document doc, ViewPlan v, Leitura lt, DimensionType dt, bool espessuras, XYZ R, XYZ U, XYZ O,
                                 List<Intervalo> ex, ResultadoVista res)
        {
            if (lt.Paredes.Count == 0) return;
            var edif = Fachada.Edificio(lt.Paredes);
            var noEdif = new HashSet<Parede>(edif);
            var planos = lt.Planos.Where(p => p.Dono != null && noEdif.Contains(p.Dono)).ToList();
            for (int k = 0; k < 2; k++) Direcao(doc, v, dt, edif, planos, k, espessuras, R, U, O, ex, res);
        }

        /// <param name="k">eixo da medida (0: cadeias horizontais, medindo as paredes verticais; 1: o contrário).</param>
        static void Direcao(Document doc, View v, DimensionType dt, List<Parede> edif, List<Plano> planos, int k, bool espessuras,
                            XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            int c = 1 - k;
            // faces laterais das paredes perpendiculares à cadeia (paralelas ao eixo c)
            var laterais = planos.Where(p => p.Eixo == k && p.Dono.Dir == c && p.B - p.A > 2 * MARGEM).ToList();
            var paredes = laterais.Select(p => p.Dono).Distinct().ToList();
            if (paredes.Count < 2) return;

            // linhas candidatas: no meio de cada faixa sem nenhuma ponta de face nem de parede (a linha não corre
            // dentro de uma parede paralela a ela nem rente a uma ponta)
            var cortes = laterais.SelectMany(p => new[] { p.A, p.B })
                .Concat(edif.SelectMany(w => new[] { w.Lo[c], w.Hi[c] })).OrderBy(x => x).ToList();
            var cand = new List<double>();
            for (int i = 0; i < cortes.Count - 1; i++)
                if (cortes[i + 1] - cortes[i] > 2 * MARGEM) cand.Add((cortes[i] + cortes[i + 1]) / 2);

            HashSet<Parede> Cobre(double y) => new HashSet<Parede>(laterais.Where(p => p.A + MARGEM / 2 <= y && y <= p.B - MARGEM / 2).Select(p => p.Dono));
            // a linha não pode atravessar uma porta: parede que passa pela linha sem face lateral nela
            bool Furo(double y, HashSet<Parede> cob) => paredes.Any(w => !cob.Contains(w) && w.Lo[c] + MARGEM < y && y < w.Hi[c] - MARGEM);

            var opcoes = cand.Select(y => (y, cob: Cobre(y))).Where(t => !Furo(t.y, t.cob)).ToList();
            var faltam = new HashSet<Parede>(paredes);
            var linhas = new List<double>();
            while (faltam.Count > 0 && linhas.Count < MAX_LINHAS && opcoes.Count > 0)
            {
                var melhor = opcoes.OrderByDescending(t => t.cob.Count(faltam.Contains)).ThenByDescending(t => t.cob.Count).First();
                if (melhor.cob.Count(faltam.Contains) == 0) break;
                linhas.Add(melhor.y);
                faltam.ExceptWith(melhor.cob);
                opcoes.Remove(melhor);
            }
            if (faltam.Count > 0)
                res.Avisos.Add(faltam.Count + " parede(s) " + (k == 0 ? "verticais" : "horizontais") + " sem cadeia interna (só atravessáveis por porta, ou mais de " + MAX_LINHAS + " linhas)");

            // cada par de paredes vizinhas uma vez só, na linha mais perto da origem (canto dos eixos: em cima e à
            // esquerda) — paredes tortas dão medidas um pouco diferentes em cada linha (pedido da FJA, 13/11/2026)
            var pares = new HashSet<(Parede, Parede)>();
            var ordem = k == 0 ? linhas.OrderByDescending(x => x) : linhas.OrderBy(x => x);
            foreach (var y in ordem) Cadeia(doc, v, dt, laterais, k, y, espessuras, pares, R, U, O, ex, res);
        }

        static void Cadeia(Document doc, View v, DimensionType dt, List<Plano> laterais, int k, double y, bool espessuras,
                           HashSet<(Parede, Parede)> pares, XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            bool hor = k == 0;
            var pts = new List<Plano>();
            foreach (var t in laterais.Where(q => q.A + MARGEM / 2 <= y && y <= q.B - MARGEM / 2)
                                      .Select(q => (f: q, x: q.PosEm(y))).OrderBy(q => q.x))
                if (pts.Count == 0 || t.x - pts[pts.Count - 1].Pos > TOL)
                    pts.Add(new Plano { Ref = t.f.Ref, Eixo = k, Pos = t.x, A = t.f.A, B = t.f.B, Dono = t.f.Dono,
                                        OK = t.f.OK, OC = t.f.OC, NK = t.f.NK, NC = t.f.NC, Arestas = t.f.Arestas });
            if (pts.Count < 2) return;

            bool Feito(Plano a, Plano b) => pares.Contains((a.Dono, b.Dono)) || pares.Contains((b.Dono, a.Dono));

            if (!espessuras)
            {
                // padrão (pedido da FJA, 13/11/2026): só os vãos — face a face entre paredes diferentes, uma cota por
                // ambiente na mesma linha; as duas faces da mesma parede (espessura) ficam sem cota
                for (int m = 0; m < pts.Count - 1; m++)
                {
                    Plano a = pts[m], b = pts[m + 1];
                    if (ReferenceEquals(a.Dono, b.Dono)) continue;
                    if (Feito(a, b)) continue;
                    pares.Add((a.Dono, b.Dono));
                    if (Motor.Medido(ex, hor, a.Pos, b.Pos)) { res.JaExistiam++; continue; }
                    if (Paralelo(a, b) && Fachada.Criar(doc, v, dt, new List<Plano> { a, b }, hor, y, R, U, O, "interna", res, true))
                    {
                        res.Segmentos++;
                        ex.Add(new Intervalo { Horizontal = hor, A = a.Pos, B = b.Pos, Linha = y });
                    }
                    else Ponte(doc, v, dt, a, b, k, y, hor, R, U, O, ex, res);
                }
                return;
            }

            // com espessura: trechos que faltam, em sequências contínuas; cada sequência dividida em grupos de faces paralelas
            int i = 0;
            while (i < pts.Count - 1)
            {
                if (Feito(pts[i], pts[i + 1])) { i++; continue; }
                if (Motor.Medido(ex, hor, pts[i].Pos, pts[i + 1].Pos)) { res.JaExistiam++; pares.Add((pts[i].Dono, pts[i + 1].Dono)); i++; continue; }
                int j = i;
                while (j < pts.Count - 1 && !Feito(pts[j], pts[j + 1]) && !Motor.Medido(ex, hor, pts[j].Pos, pts[j + 1].Pos)) j++;
                for (int m = i; m < j; m++) pares.Add((pts[m].Dono, pts[m + 1].Dono));
                Sequencia(doc, v, dt, pts.GetRange(i, j - i + 1), k, y, hor, R, U, O, ex, res);
                i = j;
            }
        }

        internal static bool Paralelo(Plano a, Plano b) => Math.Abs(a.NK * b.NC - a.NC * b.NK) < 1e-9;

        static void Sequencia(Document doc, View v, DimensionType dt, List<Plano> seq, int k, double y, bool hor,
                              XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            var grupo = new List<Plano> { seq[0] };
            for (int i = 1; i < seq.Count; i++)
            {
                if (Paralelo(grupo[0], seq[i])) { grupo.Add(seq[i]); continue; }
                Grupo(doc, v, dt, grupo, k, y, hor, R, U, O, ex, res);
                Ponte(doc, v, dt, seq[i - 1], seq[i], k, y, hor, R, U, O, ex, res);
                grupo = new List<Plano> { seq[i] };
            }
            Grupo(doc, v, dt, grupo, k, y, hor, R, U, O, ex, res);
        }

        /// <summary>Faces paralelas entre si: uma cota só; se o Revit recusar, trecho a trecho pelas quinas.</summary>
        static void Grupo(Document doc, View v, DimensionType dt, List<Plano> g, int k, double y, bool hor,
                          XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            if (g.Count < 2) return;
            if (Fachada.Criar(doc, v, dt, g, hor, y, R, U, O, "interna", res, true))
            {
                res.Segmentos += g.Count - 1;
                for (int m = 0; m < g.Count - 1; m++) ex.Add(new Intervalo { Horizontal = hor, A = g[m].Pos, B = g[m + 1].Pos, Linha = y });
                return;
            }
            for (int m = 0; m < g.Count - 1; m++) Ponte(doc, v, dt, g[m], g[m + 1], k, y, hor, R, U, O, ex, res);
        }

        /// <summary>Um trecho entre duas faces não paralelas: pelas quinas (arestas verticais) mais próximas da linha.</summary>
        static void Ponte(Document doc, View v, DimensionType dt, Plano a, Plano b, int k, double y, bool hor,
                          XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res)
        {
            var par = ParDeQuinas(a, b, k, y);
            if (par == null) { res.TrechosSemParalela++; return; }
            var (ea, eb, linha) = par.Value;
            if (Math.Abs(ea.Pos - eb.Pos) < TOL) return;
            if (Fachada.Criar(doc, v, dt, new List<Plano> { ea, eb }, hor, linha, R, U, O, "interna", res))
            {
                res.Segmentos++;
                ex.Add(new Intervalo { Horizontal = hor, A = a.Pos, B = b.Pos, Linha = linha });
            }
        }

        const double MESMA_ALTURA = 10 / 304.8;   // quinas a até 1 cm uma da outra no eixo perpendicular
        const double RECUO_LINHA = 400 / 304.8;   // linha da cota a até 40 cm das quinas, para dentro do ambiente

        /// <summary>
        /// Duas faces não paralelas: o par de quinas (uma de cada face) na mesma altura — a cota entre dois pontos sai
        /// alinhada a eles, e só fica paralela ao eixo se estiverem na mesma altura (pedido da FJA, 13/11/2026) —,
        /// o mais perto da origem (k = 0: em cima; k = 1: à esquerda). A linha fica logo ao lado das quinas, na
        /// direção de y (a linha da cadeia, dentro do ambiente). null se não houver par.
        /// </summary>
        internal static (Plano, Plano, double)? ParDeQuinas(Plano a, Plano b, int k, double y)
        {
            (Aresta, Aresta)? melhor = null;
            double nota = double.MaxValue;
            foreach (var qa in a.Arestas)
                foreach (var qb in b.Arestas)
                {
                    if (Math.Abs(qa.C - qb.C) > MESMA_ALTURA) continue;
                    double c = (qa.C + qb.C) / 2;
                    double n = k == 0 ? -c : c;   // origem: maior Y (em cima) ou menor X (à esquerda)
                    if (n < nota) { nota = n; melhor = (qa, qb); }
                }
            if (melhor == null) return null;
            var (ma, mb) = melhor.Value;
            double cm = (ma.C + mb.C) / 2;
            double linha = cm + Math.Sign(y - cm) * Math.Min(RECUO_LINHA, Math.Abs(y - cm));
            return (new Plano { Ref = ma.Ref, Eixo = k, Pos = ma.K, Dono = a.Dono, FaceRef = a.Ref },
                    new Plano { Ref = mb.Ref, Eixo = k, Pos = mb.K, Dono = b.Dono, FaceRef = b.Ref }, linha);
        }
    }
}
