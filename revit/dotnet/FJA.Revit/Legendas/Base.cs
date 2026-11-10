using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace FJA.Revit.Legendas
{
    /// <summary>Configuração de uma disciplina (equivale ao CFG dos botões em Python).</summary>
    public sealed class Disciplina
    {
        public string Botao;          // título das janelas e da transação
        public string Alvo;           // nome da vista de legenda criada/atualizada
        public string Titulo;         // texto do título dentro da legenda
        public string[] Familias;     // prefixos das famílias que entram, ex.: "(LOG)"
        public string[] Vistas;       // prefixos das vistas da disciplina (cor e verificação)
        public string[] Bases;        // início do nome das legendas que servem de base
        public string Guid;           // marca nos elementos criados (Extensible Storage)
        public string Schema;
        public string Marca;
        public byte[] Cor;            // cor quando não der para ler a do modelo
        public string Codigo;         // disciplina no Sistema FJA (cor do projeto)
        public string PrefixoLeg;     // "(LOG)" em "(LOG) (LEG) NOME"

        static readonly string[] BasesGerais = { "LOG - LEGENDA GRAFICA", "ELE - LEGENDA GRAFICA" };

        public string[] FamiliasN => Familias.Select(Texto.Norm).ToArray();
        public string[] VistasN => (Vistas ?? new string[0]).Select(Texto.Norm).ToArray();
        public string[] BasesN => (Bases ?? new string[0]).Select(Texto.Norm).Concat(BasesGerais).ToArray();
        public string Prefixo => PrefixoLeg ?? Familias[0];

        public static readonly Disciplina LOG = new Disciplina
        {
            Botao = "Legenda LOG",
            Alvo = "LOG - LEGENDA DE PONTOS DE LÓGICA",
            Titulo = "LEGENDA DE PONTOS DE LÓGICA",
            Familias = new[] { "(LOG)" },
            Vistas = new[] { "LOG" },
            Bases = new[] { "LOG - LEGENDA GRAFICA", "LOG - LEGENDA" },
            Guid = "9a4e7c21-5b3d-4e8f-a6c2-3d1f8b7e5a92",
            Schema = "FJA_LegendaLOG",
            Marca = "LegendaLOG",
            Cor = new byte[] { 121, 86, 169 },
            Codigo = "LOG",
        };

        public static readonly Disciplina AUT = new Disciplina
        {
            Botao = "Legenda AUT",
            Alvo = "AUT - LEGENDA DE PONTOS DE AUTOMAÇÃO",
            Titulo = "LEGENDA DE PONTOS DE AUTOMAÇÃO",
            Familias = new[] { "(AUT)" },
            Vistas = new[] { "AUT" },
            Bases = new[] { "AUT - LEGENDA GRAFICA", "AUT - LEGENDA" },
            Guid = "1d86c717-cc28-4626-b9c1-efb5663f1b10",
            Schema = "FJA_LegendaAUT",
            Marca = "LegendaAUT",
            Cor = new byte[] { 255, 110, 45 },
            Codigo = "AUT",
        };

        public static readonly Disciplina SON = new Disciplina
        {
            Botao = "Legenda SON",
            Alvo = "SON - LEGENDA DE PONTOS DE SONORIZAÇÃO",
            Titulo = "LEGENDA DE PONTOS DE SONORIZAÇÃO",
            Familias = new[] { "(SON)", "(SNZ)" },
            Vistas = new[] { "SON", "SNZ" },
            Bases = new[] { "SON - LEGENDA GRAFICA", "SNZ - LEGENDA GRAFICA", "SON - LEGENDA", "SNZ - LEGENDA" },
            Guid = "721db198-2a3c-40f5-bb77-93fd4d40a439",
            Schema = "FJA_LegendaSON",
            Marca = "LegendaSON",
            Cor = new byte[] { 140, 195, 25 },
            Codigo = "SON",
        };

        /// <summary>Disciplinas da ferramenta Legendas (ELE e LUM ainda pelos botões próprios, em Python).</summary>
        public static readonly Disciplina[] Todas = { LOG, AUT, SON };

        public string Sigla => Codigo;
    }

    /// <summary>Um item da legenda (um tipo de família, ou um símbolo acrescentado à mão).</summary>
    public sealed class Item
    {
        public FamilySymbol Sym;
        public FamilyInstance Rep;          // instância representante (parâmetros de visibilidade)
        public int Quantidade;
        public int Vistas;                  // plantas da disciplina em prancha onde aparece
        public bool? EmPrancha;
        public bool Extra;                  // acrescentado à mão (símbolo / detail item)
        public string Rotulo;
        public string Padrao;               // rótulo original (para saber se foi editado)
        public byte[] Cor;                  // cor mais frequente deste tipo
        public string CorOrigem = "";
        public Dictionary<string, int[]> CoresCont = new Dictionary<string, int[]>(); // "r,g,b" -> [nas plantas da disciplina, demais]
        public View Geo;                    // planta para copiar o desenho
        public FamilyInstance GeoInst;
        public byte[] CorLegenda;           // cor única resolvida para a legenda
        // resultado da montagem
        public List<ElementId> SymIds = new List<ElementId>();
        public string Modo = "";
        public string Leg;
        public bool Redimensionado, Reduzido, Ampliado;
        public double[] Mm;                 // medida no papel (largura, altura)

        public string Chave => (Extra ? "x:" : "") + Sym.UniqueId;

        /// <summary>Cópia "limpa" para outra legenda: texto original, sem resultado de montagem.</summary>
        public Item Novo()
        {
            var c = Copia();
            c.Rotulo = c.Padrao ?? c.Rotulo;
            c.CorLegenda = null;
            return c;
        }

        public Item Copia()
        {
            var c = (Item)MemberwiseClone();
            c.SymIds = new List<ElementId>();
            c.Modo = "";
            c.Leg = null;
            c.Redimensionado = c.Reduzido = c.Ampliado = false;
            c.Mm = null;
            return c;
        }
    }

    /// <summary>Tempos de cada etapa (diagnóstico de lentidão).</summary>
    public static class Tempos
    {
        static readonly Stopwatch Relogio = Stopwatch.StartNew();
        public static readonly List<Tuple<string, double>> Lista = new List<Tuple<string, double>>();
        static double _ultima;

        public static double Agora => Relogio.Elapsed.TotalSeconds;

        public static void Limpar() { Lista.Clear(); _ultima = Agora; }

        public static double Marca(string nome, double? desde = null)
        {
            double a = Agora;
            Lista.Add(Tuple.Create(nome, a - (desde ?? _ultima)));
            _ultima = a;
            return a;
        }

        public static string Texto()
        {
            return string.Join("\n", Lista.Select(t => string.Format("{0,-62} {1,7:0.00} s", t.Item1, t.Item2)));
        }
    }

    public static class Texto
    {
        /// <summary>Maiúsculas sem acento (só ASCII), como norm_ascii do Python.</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in d)
                if (c < 128) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Normalização do Sistema FJA (sem acento, só letras e números separados por espaço).</summary>
        public static string NormSistema(string s)
        {
            return Regex.Replace(Norm(s), "[^A-Z0-9]+", " ").Trim();
        }

        public static string Nome(Element e) => e == null ? "" : e.Name;

        public static string Mm(double v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace(".", ",");
    }
}
