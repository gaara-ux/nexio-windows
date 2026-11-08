using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace Telecommande
{
    /// <summary>Small JSON helpers over the framework's JavaScriptSerializer.</summary>
    static class Json
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string Write(object o) { return js.Serialize(o); }

        public static Dictionary<string, object> Obj(string s)
        {
            return js.Deserialize<Dictionary<string, object>>(s);
        }

        public static string S(IDictionary<string, object> d, string k, string def = null)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;
        }

        public static long L(IDictionary<string, object> d, string k, long def = 0)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null)
            {
                try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { }
            }
            return def;
        }

        public static bool B(IDictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v;
        }

        public static List<Dictionary<string, object>> List(IDictionary<string, object> d, string k)
        {
            var list = new List<Dictionary<string, object>>();
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is IEnumerable)
                foreach (object o in (IEnumerable)v)
                {
                    var m = o as Dictionary<string, object>;
                    if (m != null) list.Add(m);
                }
            return list;
        }
    }
}
