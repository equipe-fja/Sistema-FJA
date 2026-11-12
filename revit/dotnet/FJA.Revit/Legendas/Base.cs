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
        public BuiltInCategory[] Categorias; // tipos destas categorias entram mesmo sem a sigla (se não tiverem a de outra disciplina)
        public bool AlturaPontos = true; // LOG/AUT/SON: tomadas e pontos de parede viram um item por altura
        public bool PorAltura;        // ELE: um item por tipo + altura do ponto ("Elevação do ponto"), quadros e pontos de piso
        public Fixo[] Fixos;          // símbolos fixos que já vêm em toda legenda nova (como os acrescentados à mão)
        public bool NaCodificacao = true; // aparece na ferramenta Codificação
        public bool LegadoLum;        // apaga o que o botão antigo (Python) da legenda LUM deixou na vista

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

        public static readonly Disciplina ELE = new Disciplina
        {
            Botao = "Legenda ELE",
            Alvo = "ELE - LEGENDA DE PONTOS ELÉTRICOS",
            Titulo = "LEGENDA DE PONTOS ELÉTRICOS",
            Familias = new[] { "(ELE)" },
            Categorias = new[]
            {
                BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_TelephoneDevices,
                BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_FireAlarmDevices, BuiltInCategory.OST_NurseCallDevices,
                BuiltInCategory.OST_ElectricalEquipment,
            },
            Vistas = new[] { "ELE" },
            Bases = new[] { "ELE - LEGENDA GRAFICA", "ELE - LEGENDA" },
            // mesma marca do botão antigo em Python: a ferramenta reconhece e substitui o que ele criou
            Guid = "6f3b2c1e-8d4a-4f7b-9c2e-1a5d7e9b3c41",
            Schema = "FJA_LegendaELE",
            Marca = "LegendaELE",
            Cor = new byte[] { 65, 100, 215 },
            Codigo = "ELE",
            PorAltura = true,
            AlturaPontos = false,
            NaCodificacao = false,
            Fixos = new[]
            {
                new Fixo { Id = "sobe", Rotulo = "ELETRODUTO QUE SOBE", Procura = new[] { "ELETRODUTO QUE SOBE", "SOBE" } },
                new Fixo { Id = "desce", Rotulo = "ELETRODUTO QUE DESCE", Procura = new[] { "ELETRODUTO QUE SOBE", "DESCE" } },
                new Fixo { Id = "teto", Rotulo = "ELETRODUTO PELO TETO" },
                new Fixo { Id = "piso", Rotulo = "ELETRODUTO PELO PISO" },
                new Fixo { Id = "eletrocalha", Rotulo = "ELETROCALHA PERFURADA SEM TAMPA" },
            },
        };

        public static readonly Disciplina LUM = new Disciplina
        {
            Botao = "Legenda LUM",
            Alvo = "ELE - LEGENDA DE LUMINÁRIAS",
            Titulo = "LEGENDA DE LUMINÁRIAS",
            Familias = new[] { "(LUM)" },
            Categorias = new[] { BuiltInCategory.OST_LightingFixtures },
            Vistas = new[] { "LUM", "ELE" },
            Bases = new[] { "ELE - LEGENDA GRAFICA", "LUM - LEGENDA", "ELE - LEGENDA" },
            Guid = "c3a81f5e-7d29-4b6a-9e14-58f2d0b6a7c3",
            Schema = "FJA_LegendaLUM",
            Marca = "LegendaLUM",
            Cor = new byte[] { 255, 0, 128 },
            Codigo = "LUM",
            LegadoLum = true,
            AlturaPontos = false,
        };

        /// <summary>Disciplinas da ferramenta Legendas.</summary>
        public static readonly Disciplina[] Todas = { LOG, AUT, SON, ELE, LUM };

        /// <summary>Disciplinas da ferramenta Codificação (ELE ainda sem códigos).</summary>
        public static readonly Disciplina[] ParaCodificacao = Todas.Where(d => d.NaCodificacao).ToArray();

        public string Sigla => Codigo;
    }

    /// <summary>Símbolo fixo da legenda (ex.: eletroduto que sobe). Usa a família do projeto que bate com
    /// 'Procura' (nome da família e do tipo); sem ela, cria a anotação "(SIGLA) (FIX) RÓTULO".</summary>
    public sealed class Fixo
    {
        public string Id, Rotulo;
        public string[] Procura;      // [trecho do nome da família, trecho do nome do tipo]
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
        public string Codigo = "";          // código do ponto (Type Mark, ferramenta Codificação)
        public string Variante = "";        // variação do mesmo tipo (ex.: "BAIXA"), pelos parâmetros Sim/Não da instância
        public string VarKey = "";
        public string VarLeg;               // variação do desenho, quando difere da do item (ELE: Baixa/Média/Alta da altura)
        public int? Altura;                 // ELE: altura do ponto (mm)
        public bool SoLinhas;               // pinta só as linhas (o preenchimento tem cor própria, ex.: eletrocalha)
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

        public string Chave => (Extra ? "x:" : "") + Sym.UniqueId + (VarKey != "" ? "|" + VarKey : "");

        /// <summary>Chave da família de símbolo (LEG) deste item: por família (e variação), ou "x:" para os acrescentados.</summary>
        public string ChaveLeg => Extra ? "x:" + Sym.Id : Sym.Family.Id + (VarLegEf != "" ? "|" + VarLegEf : "");

        /// <summary>Variação do desenho (nome e chave da família (LEG)).</summary>
        public string VarLegEf => VarLeg ?? VarKey;
        public string VarLegNome => VarLeg ?? Variante;

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
