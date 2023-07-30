using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using HtmlAgilityPack;
using NoviSad.SokoBot.Data.Entities;

namespace NoviSad.SokoBot.Tools;

public static class TrainTimetableLoader {
    public static Task<IReadOnlyList<TrainTimetableRecord>> Load(TrainDirection direction, DateOnly date, CancellationToken cancellationToken) {
        int fromId, toId;
        switch (direction) {
            case TrainDirection.NoviSadToBelgrade:
                fromId = TrainStationsIds.NoviSad;
                toId = TrainStationsIds.BelgradeCentral;
                break;
            case TrainDirection.BelgradeToNoviSad:
                fromId = TrainStationsIds.BelgradeCentral;
                toId = TrainStationsIds.NoviSad;
                break;
            default:
                throw new InvalidEnumArgumentException(nameof(direction), (int)direction, typeof(TrainDirection));
        }

        return Load(fromId, toId, TimeZoneHelper.ToCentralEuropeanTime(date), cancellationToken);
    }

    private static async Task<IReadOnlyList<TrainTimetableRecord>> Load(int fromStationId, int toStationId, DateTimeOffset offset, CancellationToken cancellationToken) {
        var local = TimeZoneHelper.ToCentralEuropeanTime(offset).LocalDateTime;

        var url = $@"https://w3.srbvoz.rs/redvoznje/direktni/_/{fromStationId}/_/{toStationId}/{local:dd.MM.yyyy}/{local:HHmm}";
        var response = await url.GetAsync(cancellationToken);

        var document = new HtmlDocument();
        document.Load(await response.GetStreamAsync());

        return document.DocumentNode
            .SelectNodes("//div[@id=\"rezultati\"]/table[@class=\"tabela\"]/tr[@class=\"tsmall\"]")
            .Skip(1)
            .Select(x => {
                var td = x.SelectNodes("td");

                var number = int.Parse(td[0].InnerText.Trim());

                var t0 = TimeOnly.ParseExact(td[1].InnerText.Trim(), "HH:mm");
                var d0 = DateOnly.ParseExact(td[2].InnerText.Trim(), "dd.MM.yyyy");
                var t1 = TimeOnly.ParseExact(td[3].InnerText.Trim(), "HH:mm");
                var d1 = DateOnly.ParseExact(td[4].InnerText.Trim(), "dd.MM.yyyy");

                var tag = TryParseTag(td[7]);

                var departure = TimeZoneHelper.ToCentralEuropeanTime(d0.ToDateTime(t0));
                var arrival = TimeZoneHelper.ToCentralEuropeanTime(d1.ToDateTime(t1));

                return new TrainTimetableRecord(number, departure, arrival, tag);
            })
            .ToList();
    }

    private static string? TryParseTag(HtmlNode td) {
        var src = td.SelectSingleNode("img")?.Attributes["src"]?.Value;
        if (src == null)
            return null;

        string? name = null;
        try {
            name = Path.GetFileNameWithoutExtension(src);
        } catch {
            // ignored
        }

        return name switch {
            "RE" => "Regio Voz",
            "soko" => "Soko",
            "REx" => "Regio Voz X",
            _ => null
        };
    }
}
