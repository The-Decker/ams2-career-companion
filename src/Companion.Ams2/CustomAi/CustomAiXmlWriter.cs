using System.Globalization;
using System.Text;
using System.Xml;
using Companion.Ams2.CustomAi;

namespace Companion.Ams2.CustomAi;

/// <summary>
/// Serializes a <see cref="CustomAiFile"/> to the exact XML dialect AMS2 reads from
/// <c>&lt;install&gt;\UserData\CustomAIDrivers\&lt;VehicleClass&gt;.xml</c>: root
/// <c>&lt;custom_ai_drivers&gt;</c>, one <c>&lt;driver livery_name="..."&gt;</c> element per
/// entry with stats as child elements. UTF-8, XML-escaped (community packs break on raw
/// '&amp;' and accented characters, we never will).
/// </summary>
public static class CustomAiXmlWriter
{
    public static string ToXml(CustomAiFile file)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // A plain StringBuilder writer would make XmlWriter declare encoding="utf-16" (strings
        // are UTF-16) while WriteToDirectory saves UTF-8 bytes, a lying declaration that
        // strict parsers reject ("no Unicode byte order mark"). Utf8StringWriter keeps the
        // declaration truthful: the file IS UTF-8.
        var stringWriter = new Utf8StringWriter();
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            writer.WriteStartDocument();
            if (file.HeaderComment is { Length: > 0 } comment)
                writer.WriteComment(comment);
            writer.WriteStartElement("custom_ai_drivers");

            foreach (var driver in file.Drivers)
                WriteDriver(writer, driver);

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stringWriter.ToString();
    }

    /// <summary>StringWriter that reports UTF-8 so the XML declaration matches the encoding
    /// the file is actually saved with.</summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    public static void WriteToDirectory(CustomAiFile file, string customAiDriversDirectory)
    {
        Directory.CreateDirectory(customAiDriversDirectory);
        string path = Path.Combine(customAiDriversDirectory, file.VehicleClass + ".xml");
        File.WriteAllText(path, ToXml(file), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteDriver(XmlWriter writer, CustomAiDriver driver)
    {
        writer.WriteStartElement("driver");
        writer.WriteAttributeString("livery_name", driver.LiveryName);
        if (driver.Tracks.Count > 0)
            writer.WriteAttributeString("tracks", string.Join(",", driver.Tracks));

        WriteText(writer, "name", driver.Name);
        WriteText(writer, "country", driver.Country);

        WriteStat(writer, "race_skill", driver.RaceSkill);
        WriteStat(writer, "qualifying_skill", driver.QualifyingSkill);
        WriteStat(writer, "aggression", driver.Aggression);
        WriteStat(writer, "defending", driver.Defending);
        WriteStat(writer, "stamina", driver.Stamina);
        WriteStat(writer, "consistency", driver.Consistency);
        WriteStat(writer, "start_reactions", driver.StartReactions);
        WriteStat(writer, "wet_skill", driver.WetSkill);
        WriteStat(writer, "tyre_management", driver.TyreManagement);
        WriteStat(writer, "fuel_management", driver.FuelManagement);
        WriteStat(writer, "blue_flag_conceding", driver.BlueFlagConceding);
        WriteStat(writer, "weather_tyre_changes", driver.WeatherTyreChanges);
        WriteStat(writer, "avoidance_of_mistakes", driver.AvoidanceOfMistakes);
        WriteStat(writer, "avoidance_of_forced_mistakes", driver.AvoidanceOfForcedMistakes);
        WriteStat(writer, "vehicle_reliability", driver.VehicleReliability);

        WriteStat(writer, "weight_scalar", driver.WeightScalar);
        WriteStat(writer, "power_scalar", driver.PowerScalar);
        WriteStat(writer, "drag_scalar", driver.DragScalar);
        WriteStat(writer, "setup_downforce", driver.SetupDownforce);
        WriteStat(writer, "setup_downforce_randomness", driver.SetupDownforceRandomness);

        writer.WriteEndElement();
    }

    private static void WriteText(XmlWriter writer, string element, string? value)
    {
        if (value is { Length: > 0 })
            writer.WriteElementString(element, value);
    }

    private static void WriteStat(XmlWriter writer, string element, double? value)
    {
        if (value is { } number)
            writer.WriteElementString(element, number.ToString("0.0###", CultureInfo.InvariantCulture));
    }
}
