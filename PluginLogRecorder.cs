using System;
using System.Text;
using Dalamud.Plugin.Services;
using Serilog;
using Serilog.Events;

namespace BigFishHelper;

/// <summary>
/// Dünner Wrapper um das von Dalamud bereitgestellte IPluginLog - reicht jeden Aufruf unverändert an
/// das echte Log weiter (/xllog zeigt also weiterhin exakt dasselbe wie vorher), legt zusätzlich eine
/// formatierte Kopie in PluginLogStore ab, damit das Plugin seine eigenen Log-Zeilen auch auf einer
/// eigenen Menüseite (durchsuchbar/filterbar, siehe MainWindow.DrawLogPage) anzeigen kann, ohne
/// /xllog öffnen zu müssen (Nutzeranforderung: "Logs von /xllog in meinem Plugin... nur mein Plugin
/// betreffend"). Bewusst als Wrapper statt die hunderten bestehenden Plugin.Log.X(...)-Aufrufstellen
/// im ganzen Projekt umzuschreiben - Plugin.Log liefert einfach diese Instanz statt des rohen
/// IPluginLog, alle Aufrufstellen bleiben unverändert.
/// </summary>
public sealed class PluginLogRecorder(IPluginLog inner) : IPluginLog
{
    public ILogger Logger => inner.Logger;

    public LogEventLevel MinimumLogLevel
    {
        get => inner.MinimumLogLevel;
        set => inner.MinimumLogLevel = value;
    }

    // Die meisten Aufrufe in diesem Plugin übergeben bereits fertig interpolierte Strings ohne eigene
    // {Platzhalter} (values ist dann leer). Best effort für den seltenen Fall strukturierter Serilog-
    // Templates: einfache sequentielle Ersetzung jedes {...}-Tokens in Auftrittsreihenfolge durch den
    // jeweils nächsten Wert, unabhängig vom Platzhalternamen - kein volles Serilog-Template-Parsing
    // nötig, reicht für die Anzeige im eigenen Log-Fenster.
    private static string Format(string messageTemplate, object[] values)
    {
        if (values.Length == 0)
            return messageTemplate;

        var result = new StringBuilder();
        var valueIndex = 0;
        var i = 0;
        while (i < messageTemplate.Length)
        {
            if (messageTemplate[i] == '{' && valueIndex < values.Length)
            {
                var close = messageTemplate.IndexOf('}', i);
                if (close > i)
                {
                    result.Append(values[valueIndex] ?? "null");
                    valueIndex++;
                    i = close + 1;
                    continue;
                }
            }

            result.Append(messageTemplate[i]);
            i++;
        }

        return result.ToString();
    }

    private static void Record(LogEventLevel level, string messageTemplate, object[] values, Exception? exception = null)
    {
        var text = Format(messageTemplate, values);
        if (exception != null)
            text = $"{text}\n{exception}";
        PluginLogStore.Add(level, text);
    }

    public void Fatal(string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Fatal, messageTemplate, values);
        inner.Fatal(messageTemplate, values);
    }

    public void Fatal(Exception? exception, string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Fatal, messageTemplate, values, exception);
        inner.Fatal(exception, messageTemplate, values);
    }

    public void Error(string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Error, messageTemplate, values);
        inner.Error(messageTemplate, values);
    }

    public void Error(Exception? exception, string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Error, messageTemplate, values, exception);
        inner.Error(exception, messageTemplate, values);
    }

    public void Warning(string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Warning, messageTemplate, values);
        inner.Warning(messageTemplate, values);
    }

    public void Warning(Exception? exception, string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Warning, messageTemplate, values, exception);
        inner.Warning(exception, messageTemplate, values);
    }

    public void Information(string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Information, messageTemplate, values);
        inner.Information(messageTemplate, values);
    }

    public void Information(Exception? exception, string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Information, messageTemplate, values, exception);
        inner.Information(exception, messageTemplate, values);
    }

    public void Info(string messageTemplate, params object[] values) => Information(messageTemplate, values);

    public void Info(Exception? exception, string messageTemplate, params object[] values) => Information(exception, messageTemplate, values);

    public void Debug(string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Debug, messageTemplate, values);
        inner.Debug(messageTemplate, values);
    }

    public void Debug(Exception? exception, string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Debug, messageTemplate, values, exception);
        inner.Debug(exception, messageTemplate, values);
    }

    public void Verbose(string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Verbose, messageTemplate, values);
        inner.Verbose(messageTemplate, values);
    }

    public void Verbose(Exception? exception, string messageTemplate, params object[] values)
    {
        Record(LogEventLevel.Verbose, messageTemplate, values, exception);
        inner.Verbose(exception, messageTemplate, values);
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)
    {
        Record(level, messageTemplate, values, exception);
        inner.Write(level, exception, messageTemplate, values);
    }
}
