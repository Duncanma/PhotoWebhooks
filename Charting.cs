using QuickChart;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhotoWebhooks
{
    public static class Charting
    {
        public static string Make7DayLineChart(Dictionary<string, string> data)
        {
            Chart qc = new Chart();

			string labels = "";
            string range = "";
            int maxY = 80;
            foreach (var key in data.Keys)
			{
				DateTimeOffset date = DateTimeOffset.ParseExact(key, "yyyyMMdd", CultureInfo.InvariantCulture);
				labels += $" '{date:M-dd}', ";
				string value = data[key];
                int valueAsInt = Convert.ToInt32(value);
                if (valueAsInt > maxY)
                {
                    maxY = valueAsInt;
                }
			}

            maxY += 20;

            maxY = (maxY / 10) * 10;

			labels.Substring(0, labels.Length - 2);
			range = String.Join(", ", data.Values.Select(x => x.ToString()));



            qc.Width = 800;
            qc.Height = 300;
            qc.Version = "4";
            string jsonData= @"{type: 'line', data: { labels: [" + labels 
							+ @"], datasets: [{label: 'Recent Views', backgroundColor: '#6371ff', borderColor: '#6371ff', data: ["
							+ range + @"], fill: false, lineTension: 0.3 }, ], }, options: { scales: { y: { min: 0, max: " + maxY.ToString() + " }, x: { offset:true} }, " +
						    @"plugins: { datalabels: { anchor: 'center', align: 'center', color: '#000', backgroundColor: '#bbb', " +
							@"borderColor: '#000', borderWidth: 1, borderRadius: 5, },}, title: { display: true, text: 'Past 7 days of views', },},}";
            qc.Config = jsonData;
			// Get the URL
			return qc.GetUrl();

            // Or get the image
            //byte[] imageBytes = qc.ToByteArray();

            // Or write it to a file
            //qc.ToFile("chart.png");

        }

    }
}
