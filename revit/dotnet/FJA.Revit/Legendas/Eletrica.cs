using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace FJA.Revit.Legendas
{
    /// <summary>Regras da legenda ELE (as mesmas do botão antigo em Python): pontos de parede por tipo + altura,
    /// pontos de piso, caixas de passagem e quadros.</summary>
    public static class Eletrica
    {
        public const int PAREDE = 0, PISO = 1, CAIXA = 2, QUADRO = 3;

        static readonly string[] Excluir = { "NAO LISTAV" };
        static readonly string[] PalavrasCaixa = { "CAIXA DE PASSAGEM", "CAIXA PASSAGEM" };
        const string PalavraPiso = "PISO";
        static readonly string[] PalavrasQuadro = { "QUADRO", "PAINEL", "PANEL", "QDC", "QGBT" };
        const string ParamAltura = "ELEVACAO DO PONTO";

        static string NomeTipo(Element e) { try { return e.Name ?? ""; } catch { return ""; } }

        /// <summary>A instância entra na legenda ELE? Devolve o bloco e a altura (mm, ao cm; só pontos de parede).</summary>
        public static bool Inclui(FamilyInstance i, out int bloco, out int? altura)
        {
            bloco = PAREDE; altura = null;
            var s = i.Symbol;
            if (s == null) return false;
            string fam = Texto.Norm(s.FamilyName);
            if (i.Category != null && i.Category.Id == new ElementId(BuiltInCategory.OST_ElectricalEquipment))
            {
                string n = fam + " " + Texto.Norm(NomeTipo(s));
                if (!PalavrasQuadro.Any(w => n.Contains(w))) return false;
                bloco = QUADRO;
                return true;
            }
            if (Excluir.Any(w => fam.Contains(w))) return false;
            if (PalavrasCaixa.Any(w => fam.Contains(w))) { bloco = CAIXA; return true; }
            if (fam.Contains(PalavraPiso)) { bloco = PISO; return true; }
            altura = Altura(i);
            return altura != null;   // sem "Elevação do ponto": não é ponto de parede
        }

        public static int? Altura(FamilyInstance i)
        {
            foreach (Parameter p in i.Parameters)
            {
                try
                {
                    if (p.StorageType == StorageType.Double && Texto.Norm(p.Definition.Name) == ParamAltura)
                        return (int)Math.Round(p.AsDouble() * 304.8 / 10.0) * 10;
                }
                catch { }
            }
            return null;
        }

        static int Grupo(string n)
        {
            bool t = n.Contains("TOMADA"), it = n.Contains("INTERRUPTOR");
            return t && it ? 2 : t ? 1 : it ? 3 : 4;
        }

        static readonly Dictionary<string, int> Mult = new Dictionary<string, int>
        {
            { "DUPLA", 2 }, { "DUPLO", 2 }, { "DUPLAS", 2 }, { "DUPLOS", 2 },
            { "TRIPLA", 3 }, { "TRIPLO", 3 }, { "TRIPLAS", 3 }, { "TRIPLOS", 3 },
        };

        /// <summary>Pontos na mesma caixa: "2 TOMADAS ..." = 2; "1 INTERRUPTOR ... + 1 TOMADA ..." = 2.</summary>
        static int PontosNaCaixa(string b)
        {
            int total = 0;
            foreach (var parte0 in Regex.Split(b, @"\s\+\s"))
            {
                string parte = parte0.Trim();
                var m = Regex.Match(parte, @"^(\d+)\s");
                int n = m.Success ? int.Parse(m.Groups[1].Value) : 1;
                foreach (Match w in Regex.Matches(parte, "[A-Z]+"))
                    if (Mult.TryGetValue(w.Value, out int k)) { n *= k; break; }
                total += n;
            }
            return total;
        }

        /// <summary>Texto base: a Description, se for informativa; senão o nome do tipo (sem "a. " de ordenação).</summary>
        static string Base(FamilySymbol s)
        {
            string desc = "";
            try { desc = (s.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "").Trim(); } catch { }
            if (desc.Length < 8 || Regex.IsMatch(Texto.Norm(desc), "^\\d+\\s*X\\s*\\d+\"?$")) desc = NomeTipo(s);
            desc = Regex.Replace(desc.Trim(), @"^[A-Za-z]\.\s*", "");
            return desc.ToUpperInvariant().Trim();
        }

        public static string Rotulo(FamilySymbol s, int bloco, int? altura)
        {
            if (bloco == QUADRO)
            {
                string desc = "";
                try { desc = (s.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "").Trim(); } catch { }
                string t = (desc.Length >= 8 ? desc : NomeTipo(s)).ToUpperInvariant().Trim();
                var mq = Regex.Match(t, @"^([A-Z0-9]{2,6})\s*-\s*(.+)$");
                return mq.Success ? mq.Groups[1].Value + " - " + mq.Groups[2].Value.Trim() : t;
            }
            string b = Base(s);
            string caixa = null;
            var m = Regex.Match(b, "\\s*-\\s*(\\d+\\s*X\\s*\\d+)\"?\\s*$");
            if (m.Success)
            {
                caixa = m.Groups[1].Value.Replace(" ", "");
                b = b.Substring(0, m.Index).Trim();
            }
            var partes = new List<string> { b };
            if (altura != null) partes.Add("A " + (altura.Value / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace(".", ",") + "M DO PISO");
            if (caixa != null) partes.Add("EM CAIXA " + caixa + "\"");
            return string.Join(", ", partes);
        }

        /// <summary>Ordem da legenda: parede, piso, caixas, quadros; tomadas, tomada + interruptor, interruptores,
        /// outros; pontos na caixa; nome; altura.</summary>
        public static string Ordem(FamilySymbol s, int bloco, int? altura)
        {
            string b = Texto.Norm(Base(s));
            string n = Texto.Norm(s.FamilyName + " " + NomeTipo(s));
            string semQtd = Regex.Replace(b, @"^\d+\s+", "");
            return bloco + "|" + Grupo(n) + "|" + PontosNaCaixa(b).ToString("000") + "|" + semQtd + "|" + (altura ?? 0).ToString("00000");
        }
    }
}
