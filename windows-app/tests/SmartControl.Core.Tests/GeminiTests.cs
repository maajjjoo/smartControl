using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;
using SmartControl.Core.Transport;

namespace SmartControl.Core.Tests;

/// <summary>
/// Gemini como IA de los gestos, con un servicio FALSO: ninguna prueba sale a internet ni usa una
/// clave real. Se comprueba qué se envía, cómo se lee la respuesta y que, si Gemini falla, la app
/// sigue funcionando con las reglas del reloj.
/// </summary>
internal static class GeminiTests
{
    private const string Device = "watch-gemini-01";
    // Valores ficticios solo para pruebas; no son credenciales reales.
    private const string Token = "gemini-test-token-not-a-secret";
    private const string ApiKey = "fake-gemini-key-for-tests";

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("gemini: decide el gesto y la respuesta dice que fue la IA", GeminiDecides);
        yield return ("gemini: solo recibe los números del giroscopio, con la clave en la cabecera", SendsOnlySensorNumbers);
        yield return ("gemini: «no es un gesto» o poca confianza no hacen nada", NoGestureOrLowConfidenceDoNothing);
        yield return ("gemini: si falla, tarda o responde raro, deciden las reglas", FallsBackToRules);
        yield return ("gemini: un modelo local instalado tiene prioridad", LocalModelHasPriority);
        yield return ("gemini: nombre de modelo y clave se validan", ValidatesConfiguration);
    }

    /// <summary>Gemini falso: responde con <see cref="Reply"/> y guarda la última petición.</summary>
    private sealed class FakeGemini : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Reply { get; set; } = () => Answer("TWIST_OUT", 0.93);
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, ct);
            return Reply();
        }

        public static HttpResponseMessage Answer(string gesture, double confidence) => Raw(
            "{\"gesture\":\"" + gesture + "\",\"confidence\":" + confidence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");

        public static HttpResponseMessage Raw(string modelText) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { parts = new[] { new { text = modelText } } } } },
            }), Encoding.UTF8, "application/json"),
        };
    }

    private static (GestureRecognizer Recognizer, FakeGemini Fake) Recognizer(TimeSpan? timeout = null, DenseClassifier? local = null)
    {
        var registry = new PairedDeviceRegistry();
        registry.AddSessionDevice(Device, Token, "Reloj");
        var fake = new FakeGemini();
        var gemini = new GeminiGestureClassifier(ApiKey, "gemini-2.5-flash", new HttpClient(fake), timeout ?? TimeSpan.FromSeconds(2));
        return (new GestureRecognizer(registry, new CommandValidator(registry), local, gemini), fake);
    }

    private static byte[] Window(string mode = "PRESENTATION", string detector = "TWIST_IN") =>
        Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":\"1.0-draft\",\"messageId\":\"" + Guid.NewGuid().ToString("D") +
            "\",\"timestampUtc\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            ",\"deviceId\":\"" + Device + "\",\"sessionToken\":\"" + Token + "\",\"messageType\":\"SENSOR_WINDOW\",\"payload\":{" +
            "\"activeMode\":\"" + mode + "\",\"detectorEvent\":\"" + detector + "\",\"detectorConfidence\":0.9,\"gyroscope\":[" +
            string.Join(",", Enumerable.Range(0, 25).Select(i => $"[{i * 20},{(i is >= 5 and < 10 ? 4.5 : 0.0).ToString(System.Globalization.CultureInfo.InvariantCulture)},0.1,-0.05]")) +
            "]}}");

    private static async Task GeminiDecides()
    {
        var (recognizer, _) = Recognizer();
        Check.True(recognizer.HasAi && !recognizer.HasModel, "IA remota, sin modelo local");
        Check.Equal("Gemini (gemini-2.5-flash)", recognizer.AiName);
        // Las reglas dicen «hacia dentro»; Gemini dice «hacia fuera»: manda Gemini.
        var result = await recognizer.RecognizeAsync(Window("MULTIMEDIA", "TWIST_IN"), CancellationToken.None);
        Check.True(result.Result.IsAccepted, $"debe aceptarse: {result.Result.ReasonCode}");
        Check.Equal<AllowedAction?>(AllowedAction.MediaNextTrack, result.Result.Action);
        Check.Equal(RecognitionInfo.ByModel, result.Info!.RecognizedBy);
        Check.Equal("TWIST_OUT", result.Info.Gesture);
        Check.Equal(0.93, result.Info.Confidence);
        Check.Equal("Gemini (gemini-2.5-flash)", result.Info.Engine);
    }

    private static async Task SendsOnlySensorNumbers()
    {
        var (recognizer, fake) = Recognizer();
        await recognizer.RecognizeAsync(Window(), CancellationToken.None);
        var request = fake.LastRequest!;
        Check.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", request.RequestUri!.ToString());
        Check.Equal(ApiKey, request.Headers.GetValues("x-goog-api-key").Single());
        Check.True(!request.RequestUri.Query.Contains("key", StringComparison.OrdinalIgnoreCase), "la clave no va en la dirección");
        var body = fake.LastBody!;
        Check.True(!body.Contains(Token) && !body.Contains(Device) && !body.Contains(ApiKey), "ni clave de sesión, ni dispositivo, ni clave de Gemini en el texto");
        Check.True(!body.Contains("PRESENTATION") && !body.Contains("detector", StringComparison.OrdinalIgnoreCase), "ni el modo ni la opinión de las reglas");
        Check.True(body.Contains("100,4.5,0.1,-0.05"), "sí las muestras del giroscopio");
        Check.True(body.Contains("responseSchema") && body.Contains("NO_GESTURE"), "respuesta obligada a un esquema");
    }

    private static async Task NoGestureOrLowConfidenceDoNothing()
    {
        var (recognizer, fake) = Recognizer();
        fake.Reply = () => FakeGemini.Answer("NO_GESTURE", 0.95);
        var none = await recognizer.RecognizeAsync(Window(), CancellationToken.None);
        Check.Equal(RecognitionReasons.NoGesture, none.Result.ReasonCode);
        Check.Equal(CommandStatus.RejectedLowConfidence, none.Result.Status);

        fake.Reply = () => FakeGemini.Answer("TWIST_OUT", 0.6);
        var unsure = await recognizer.RecognizeAsync(Window(), CancellationToken.None);
        Check.Equal(RecognitionReasons.LowConfidence, unsure.Result.ReasonCode);
        Check.Equal(RecognitionInfo.ByModel, unsure.Info!.RecognizedBy);
    }

    private static async Task FallsBackToRules()
    {
        var failures = new (string Name, Func<HttpResponseMessage> Reply, TimeSpan Delay)[]
        {
            ("error del servicio", () => new HttpResponseMessage(HttpStatusCode.InternalServerError), TimeSpan.Zero),
            ("clave rechazada", () => new HttpResponseMessage(HttpStatusCode.Forbidden), TimeSpan.Zero),
            ("texto que no es JSON", () => FakeGemini.Raw("hacia fuera, seguro"), TimeSpan.Zero),
            ("clase inventada", () => FakeGemini.Answer("SALTAR", 0.99), TimeSpan.Zero),
            ("confianza fuera de rango", () => FakeGemini.Answer("TWIST_OUT", 7), TimeSpan.Zero),
            ("sin internet", () => throw new HttpRequestException("sin red"), TimeSpan.Zero),
            ("demasiado lento", () => FakeGemini.Answer("TWIST_OUT", 0.99), TimeSpan.FromSeconds(2)),
        };
        foreach (var (name, reply, delay) in failures)
        {
            var (recognizer, fake) = Recognizer(timeout: TimeSpan.FromMilliseconds(300));
            fake.Reply = reply;
            fake.Delay = delay;
            var result = await recognizer.RecognizeAsync(Window("PRESENTATION", "TWIST_IN"), CancellationToken.None);
            Check.Equal(RecognitionInfo.ByRules, result.Info!.RecognizedBy, name);
            Check.Equal<AllowedAction?>(AllowedAction.SlidePrevious, result.Result.Action, $"{name}: decide lo que dijeron las reglas");
        }
    }

    private static async Task LocalModelHasPriority()
    {
        var local = new DenseClassifier(ModelFixtures.Parse(ModelFixtures.LogisticJson()));
        var (recognizer, fake) = Recognizer(local: local);
        var result = await recognizer.RecognizeAsync(Window(), CancellationToken.None);
        Check.Equal(0, fake.Calls, "no se llama a Gemini");
        Check.True(result.Info!.Engine!.StartsWith("modelo ", StringComparison.Ordinal), "decidió el modelo local");
    }

    private static Task ValidatesConfiguration()
    {
        Check.Throws<ArgumentException>(() => new GeminiGestureClassifier(""));
        Check.Throws<ArgumentException>(() => new GeminiGestureClassifier(ApiKey, "../../otra-cosa?key=1"));
        Check.Throws<ArgumentException>(() => new GeminiGestureClassifier(ApiKey, "Gemini Pro"));
        Check.Equal("gemini-3.5-flash-lite", new GeminiGestureClassifier(ApiKey).Model);
        Check.Equal("gemini-2.0-flash-lite", new GeminiGestureClassifier(ApiKey, " gemini-2.0-flash-lite ").Model);
        return Task.CompletedTask;
    }
}
