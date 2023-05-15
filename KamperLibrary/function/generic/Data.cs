using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KamperLibrary.function.generic
{
    public static class Data
    {
        public static String NormalizeApiJsonData(String Incoming, String CampsiteID, DateTime CheckDt)
        {
            String ReturnStr = Incoming;
            var cachepath = KamperLibrary.function.generic.Cache.GetCachPath();
            String JsonFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-CampsitesByPark.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                List<String> CampsiteIds = JsonSerializer.Deserialize<List<String>>(json);
                foreach (String ThisCampsiteId in CampsiteIds)
                {
                    ReturnStr = ReturnStr.Replace("\"" + ThisCampsiteId + "\":{", "{");
                }
            }
            Int32 DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
            int counter = 0;
            while (counter < DaysInCurrentMonth)
            {
                counter++;
                String CounterStr = counter.ToString();
                if (CounterStr.Length == 1)
                {
                    CounterStr = "0" + CounterStr;
                }
                String MonthStr = CheckDt.ToString("MM");
                String FormattedDate = $"\"{CheckDt.Year.ToString()}-{MonthStr}-{CounterStr}T00:00:00Z\":";
                ReturnStr = ReturnStr
                    .Replace(FormattedDate, "\"availability\":")
                    .Replace(",\"availability\"", "},{\"availability\"");
            }
            return ReturnStr
                .Replace("\"campsites\":{", "\"campsites\":[")
                .Replace("}}", "}]")
                .Replace("\"availabilities\":{", "\"availabilities\":[{")
                .Replace("},\"campsite_id\":", "}],\"campsite_id\":")
                .Replace("\"quantities\":{", "\"quantities\":[{")
                .Replace("},\"site\":", "}],\"site\":");
        }
    }
}