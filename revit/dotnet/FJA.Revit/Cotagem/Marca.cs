using System;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Extensible Storage da cotagem:
    /// - FJA_Cotagem, em cada cota criada pela ferramenta (o modo "Refazer as minhas" só apaga estas);
    /// - FJA_CotagemVista, na vista, com o tipo escolhido (volta pré-selecionado na próxima vez).
    /// </summary>
    public static class Marca
    {
        static readonly Guid GUID_COTA = new Guid("8c3f2a61-4d7e-4b19-a0c5-2e9b7d41f6a3");
        static readonly Guid GUID_VISTA = new Guid("d71b05e4-93a2-4c68-bf1d-6a4e28c9035f");

        static Schema Esquema(Guid g, string nome, string doc, params string[] campos)
        {
            Schema sc = Schema.Lookup(g);
            if (sc != null) return sc;
            var sb = new SchemaBuilder(g);
            sb.SetSchemaName(nome);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation(doc);
            foreach (var c in campos) sb.AddSimpleField(c, typeof(string));
            return sb.Finish();
        }

        static Schema EsquemaCota() => Esquema(GUID_COTA, "FJA_Cotagem", "Cota criada pela ferramenta Cotagem das Ferramentas FJA", "regra", "criadoEm");
        static Schema EsquemaVista() => Esquema(GUID_VISTA, "FJA_CotagemVista", "Tipo da vista escolhido na ferramenta Cotagem das Ferramentas FJA", "tipo");

        public static void MarcarCota(Element cota, string regra)
        {
            var sc = EsquemaCota();
            var e = new Entity(sc);
            e.Set(sc.GetField("regra"), regra ?? "");
            e.Set(sc.GetField("criadoEm"), DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            cota.SetEntity(e);
        }

        public static bool DaFerramenta(Element cota)
        {
            var sc = Schema.Lookup(GUID_COTA);
            if (sc == null) return false;
            try { var e = cota.GetEntity(sc); return e != null && e.IsValid(); }
            catch { return false; }
        }

        public static TipoVista TipoGravado(View v)
        {
            var sc = Schema.Lookup(GUID_VISTA);
            if (sc == null) return TipoVista.Indefinido;
            try
            {
                var e = v.GetEntity(sc);
                if (e == null || !e.IsValid()) return TipoVista.Indefinido;
                return Enum.TryParse(e.Get<string>("tipo"), out TipoVista t) ? t : TipoVista.Indefinido;
            }
            catch { return TipoVista.Indefinido; }
        }

        public static void GravarTipo(View v, TipoVista t)
        {
            var sc = EsquemaVista();
            var e = new Entity(sc);
            e.Set(sc.GetField("tipo"), t.ToString());
            v.SetEntity(e);
        }
    }
}
