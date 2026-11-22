using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartControl.Core.Recognition;

/// <summary>Respuesta de un clasificador remoto: el gesto y su confianza en [0, 1].</summary>
public sealed record RemoteGestureAnswer(GestureClass Gesture, double Confidence);

/// <summary>Clasificador de gestos que se consulta por la red (por ejemplo, Gemini).</summary>
public interface IRemoteGestureClassifier
{
    /// <summary>Nombre que se muestra en la ventana, p. ej. «Gemini (gemini-3.5-flash-lite)».</summary>
    string Name { get; }

    /// <summary>
    /// Clasifica la ventana del giroscopio. Devuelve null si no hay respuesta válida a tiempo (sin
    /// internet, error del servicio, respuesta fuera de formato): entonces deciden las reglas.
    /// </summary>
    Task<RemoteGestureAnswer?> ClassifyAsync(IReadOnlyList<SensorSample> gyroscope, CancellationToken ct);
}

/// <summary>
/// Clasifica la ventana de un movimiento con la API de Gemini (decisión del equipo del
/// 2026-11-22, ADR-07). Solo se envían los números del giroscopio: ni identificadores, ni claves,
/// ni el modo ni nada del usuario. La respuesta está obligada a un esquema JSON con tres valores
/// posibles; cualquier otra cosa se descarta. La clave nunca se registra.
/// </summary>
public sealed class GeminiGestureClassifier : IRemoteGestureClassifier
{
    /// <summary>
    /// Modelo por defecto. Elegido el 2026-11-22 probando con la clave del equipo: acertó 12 de 12
    /// movimientos de prueba en 0,6–1,1 s. Los modelos «flash» sin «lite» razonan antes de responder
    /// y tardaban hasta 10 s (más que el tiempo máximo de espera); gemini-2.5-flash ya no está
    /// disponible para cuentas nuevas. Se cambia con GEMINI_MODEL en el .env.
    /// </summary>
    public const string DefaultModel = "gemini-3.5-flash-lite";
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/";
    private static readonly Regex ModelPattern = new(@"\A[a-z0-9][a-z0-9.\-]{0,63}\z", RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly TimeSpan _timeout;

    public GeminiGestureClassifier(string apiKey, string? model = null, HttpClient? http = null, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Falta la clave de Gemini.", nameof(apiKey));
        _apiKey = apiKey.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        // El nombre va en la dirección web: solo letras minúsculas, números, puntos y guiones.
        if (!ModelPattern.IsMatch(_model))
            throw new ArgumentException("Nombre de modelo de Gemini no válido.", nameof(model));
        _http = http ?? new HttpClient();
        _timeout = timeout ?? TimeSpan.FromSeconds(2.5);
    }

    public string Name => $"Gemini ({_model})";

    public string Model => _model;

    public async Task<RemoteGestureAnswer?> ClassifyAsync(IReadOnlyList<SensorSample> gyroscope, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + _model + ":generateContent")
            {
                Content = new StringContent(RequestBody(gyroscope), Encoding.UTF8, "application/json"),
            };
            // En la cabecera, no en la dirección: así la clave no aparece en registros de URL.
            request.Headers.Add("x-goog-api-key", _apiKey);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ParseResponse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Petición a Gemini: instrucciones, resumen numérico y muestras, con salida JSON obligatoria.</summary>
    internal static string RequestBody(IReadOnlyList<SensorSample> gyroscope)
    {
        var options = new JsonWriterOptions();
        using var buffer = new System.IO.MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, options))
        {
            json.WriteStartObject();
            json.WritePropertyName("contents");
            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteString("role", "user");
            json.WritePropertyName("parts");
            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteString("text", Prompt(gyroscope));
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();

            json.WritePropertyName("generationConfig");
            json.WriteStartObject();
            json.WriteNumber("temperature", 0);
            json.WriteString("responseMimeType", "application/json");
            json.WritePropertyName("responseSchema");
            json.WriteStartObject();
            json.WriteString("type", "OBJECT");
            json.WritePropertyName("properties");
            json.WriteStartObject();
            json.WritePropertyName("gesture");
            json.WriteStartObject();
            json.WriteString("type", "STRING");
            json.WritePropertyName("enum");
            json.WriteStartArray();
            json.WriteStringValue("TWIST_OUT");
            json.WriteStringValue("TWIST_IN");
            json.WriteStringValue("NO_GESTURE");
            json.WriteEndArray();
            json.WriteEndObject();
            json.WritePropertyName("confidence");
            json.WriteStartObject();
            json.WriteString("type", "NUMBER");
            json.WriteEndObject();
            json.WriteEndObject();
            json.WritePropertyName("required");
            json.WriteStartArray();
            json.WriteStringValue("gesture");
            json.WriteStringValue("confidence");
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Prompt(IReadOnlyList<SensorSample> g)
    {
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var peakX = g.Max(s => s.X);
        var minX = g.Min(s => s.X);
        var maxOther = g.Max(s => Math.Max(Math.Abs(s.Y), Math.Abs(s.Z)));
        var area = 0.0;
        for (var i = 1; i < g.Count; i++)
            area += g[i].X * (g[i].TOffsetMs - g[i - 1].TOffsetMs) / 1000.0;
        var text = new StringBuilder();
        text.AppendLine("Clasifica un movimiento de muñeca medido por el giroscopio de un reloj inteligente.");
        text.AppendLine("Unidades: rad/s. El eje X va a lo largo del antebrazo: girar la muñeca rota sobre X.");
        text.AppendLine("Clases:");
        text.AppendLine("- TWIST_OUT: un giro rápido y único hacia fuera: un pico claro de X positivo (normalmente más de 3 rad/s) que domina sobre Y y Z, y vuelve al reposo.");
        text.AppendLine("- TWIST_IN: igual pero hacia dentro: un pico claro de X negativo que domina sobre Y y Z.");
        text.AppendLine("- NO_GESTURE: cualquier otra cosa: caminar, escribir, beber, sacudir (X alterna de signo varias veces), giros lentos o débiles, o movimiento repartido en Y y Z.");
        text.AppendLine("Responde con la clase y tu confianza entre 0 y 1. Si dudas, usa NO_GESTURE o una confianza baja: un error ejecuta una acción en el ordenador.");
        text.AppendLine($"Resumen: muestras={g.Count}, duración_ms={g[^1].TOffsetMs}, x_máx={F(peakX)}, x_mín={F(minX)}, máx_|y|_|z|={F(maxOther)}, área_x={F(area)} rad.");
        text.AppendLine("Muestras (t_ms,x,y,z):");
        foreach (var s in g)
            text.Append(s.TOffsetMs).Append(',').Append(F(s.X)).Append(',').Append(F(s.Y)).Append(',').Append(F(s.Z)).Append('\n');
        return text.ToString();
    }

    /// <summary>Lee la respuesta de Gemini; null si no tiene exactamente el formato pedido.</summary>
    internal static RemoteGestureAnswer? ParseResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            return null;
        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
            return null;

        using var answer = JsonDocument.Parse(textElement.GetString()!);
        var root = answer.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("gesture", out var gesture) || gesture.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("confidence", out var confidence) || !confidence.TryGetDouble(out var value)
            || !(value >= 0 && value <= 1))
            return null;
        GestureClass? kind = gesture.GetString() switch
        {
            "TWIST_OUT" => GestureClass.TwistOut,
            "TWIST_IN" => GestureClass.TwistIn,
            "NO_GESTURE" => GestureClass.NoGesture,
            _ => null,
        };
        return kind is null ? null : new RemoteGestureAnswer(kind.Value, value);
    }
}
