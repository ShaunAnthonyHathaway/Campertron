using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KampLibrary.function.generic
{
    public static class Convert
    {
        public static string StateNameToAbbreviation(string stateName)
        {
            switch (stateName.ToUpper())
            {
                case "ALABAMA":
                    return "AL";
                case "ALASKA":
                    return "AK";
                case "ARIZONA":
                    return "AZ";
                case "ARKANSAS":
                    return "AR";
                case "CALIFORNIA":
                    return "CA";
                case "COLORADO":
                    return "CO";
                case "CONNECTICUT":
                    return "CT";
                case "DELAWARE":
                    return "DE";
                case "FLORIDA":
                    return "FL";
                case "GEORGIA":
                    return "GA";
                case "HAWAII":
                    return "HI";
                case "IDAHO":
                    return "ID";
                case "ILLINOIS":
                    return "IL";
                case "INDIANA":
                    return "IN";
                case "IOWA":
                    return "IA";
                case "KANSAS":
                    return "KS";
                case "KENTUCKY":
                    return "KY";
                case "LOUISIANA":
                    return "LA";
                case "MAINE":
                    return "ME";
                case "MARYLAND":
                    return "MD";
                case "MASSACHUSETTS":
                    return "MA";
                case "MICHIGAN":
                    return "MI";
                case "MINNESOTA":
                    return "MN";
                case "MISSISSIPPI":
                    return "MS";
                case "MISSOURI":
                    return "MO";
                case "MONTANA":
                    return "MT";
                case "NEBRASKA":
                    return "NE";
                case "NEVADA":
                    return "NV";
                case "NEW HAMPSHIRE":
                    return "NH";
                case "NEW JERSEY":
                    return "NJ";
                case "NEW MEXICO":
                    return "NM";
                case "NEW YORK":
                    return "NY";
                case "NORTH CAROLINA":
                    return "NC";
                case "NORTH DAKOTA":
                    return "ND";
                case "OHIO":
                    return "OH";
                case "OKLAHOMA":
                    return "OK";
                case "OREGON":
                    return "OR";
                case "PENNSYLVANIA":
                    return "PA";
                case "RHODE ISLAND":
                    return "RI";
                case "SOUTH CAROLINA":
                    return "SC";
                case "SOUTH DAKOTA":
                    return "SD";
                case "TENNESSEE":
                    return "TN";
                case "TEXAS":
                    return "TX";
                case "UTAH":
                    return "UT";
                case "VERMONT":
                    return "VT";
                case "VIRGINIA":
                    return "VA";
                case "WASHINGTON":
                    return "WA";
                case "WEST VIRGINIA":
                    return "WV";
                case "WISCONSIN":
                    return "WI";
                case "WYOMING":
                    return "WY";
                default:
                    return stateName;
            }
        }
    }
}
