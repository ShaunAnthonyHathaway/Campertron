using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

[Keyless]
[NotMapped]
public class GEOJSON
{
    public String? TYPE { get; set; }
    public List<Double>? COORDINATES { get; set; }
}