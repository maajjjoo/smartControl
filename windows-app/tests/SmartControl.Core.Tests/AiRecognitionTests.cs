using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Pairing;
using SmartControl.Core.Protocol;
using SmartControl.Core.Recognition;
using SmartControl.Core.Transport;

namespace SmartControl.Core.Tests;

/// <summary>
/// SENSOR_WINDOW de punta a punta por TCP: el reloj envía la ventana del movimiento y Windows
/// decide con la IA (modelo SINTÉTICO de prueba) o, sin modelo, con lo que dijeron las reglas.
/// El modelo sintético comprueba el código, no la precisión.
/// </summary>
internal static class AiRecognitionTests
{
    private const string Device = "watch-ai-01";
    // Valor ficticio solo para pruebas; no es una credencial real.
    private const string Token = "sensor-window-test-token-not-a-secret";

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("ia: sin modelo deciden las reglas del reloj", RulesDecideWithoutModel);
        yield return ("ia: sin modelo, un movimiento poco claro no hace nada", UnclearDoesNothingWithoutModel);
        yield return ("ia: con modelo decide la IA, aunque las reglas digan otra cosa", ModelDecides);
        yield return ("ia: el modelo descarta un movimiento normal", ModelRejectsNoGesture);
        yield return ("ia: solo un reloj vinculado, y sin repeticiones", NeedsPairingAndRejectsReplay);
        yield return ("ia: ventanas mal formadas se rechazan", MalformedWindowsAreRejected);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(bool withModel)
        {
            var registry = new PairedDeviceRegistry();
            registry.AddSessionDevice(Device, Token, "Reloj");
            var validator = new CommandValidator(registry);
            var classifier = withModel ? new DenseClassifier(ModelFixtures.Parse(ModelFixtures.LogisticJson())) : null;
            Recognizer = new GestureRecognizer(registry, validator, classifier);
            Receiver = new LocalCommandReceiver(validator, Executor, Log, port: 0, recognizer: Recognizer);
            Receiver.CommandProcessed += (_, e) => { lock (Events) Events.Add(e); };
            Receiver.Start();
        }

        public GestureRecognizer Recognizer { get; }
        public LocalCommandReceiver Receiver { get; }
        public InMemoryEventLog Log { get; } = new();
        public List<CommandProcessedEventArgs> Events { get; } = new();
        public RecordingExecutor Executor { get; } = new();

        public async Task<JsonElement> Send(string message)
        {
            var response = await LoopbackTestClient.SendAsync(Receiver.Port, Encoding.UTF8.GetBytes(message), TimeSpan.FromSeconds(5));
            return JsonDocument.Parse(response).RootElement.GetProperty("payload").Clone();
        }

        public ValueTask DisposeAsync() => Receiver.DisposeAsync();
    }

    internal sealed class RecordingExecutor : ICommandExecutor
    {
        public List<AllowedAction> Done { get; } = new();
        public bool PerformsRealActions => true;

        public string Execute(AllowedAction action)
        {
            lock (Done) Done.Add(action);
            return "hecho";
        }
    }

    /// <summary>Reposo, meseta de <paramref name="plateau"/> rad/s en X durante 5 muestras y reposo (como el reloj).</summary>
    private static string Gyroscope(double plateau) =>
        "[" + string.Join(",", Enumerable.Range(0, 25).Select(i =>
            $"[{i * 20},{(i is >= 5 and < 10 ? plateau : 0.0).ToString(CultureInfo.InvariantCulture)},0.0,0.0]")) + "]";

    internal static string Window(string mode, string detector, double plateau, string? messageId = null, string token = Token, string? gyroscope = null) =>
        "{\"protocolVersion\":\"1.0-draft\",\"messageId\":\"" + (messageId ?? Guid.NewGuid().ToString("D")) +
        "\",\"timestampUtc\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
        ",\"deviceId\":\"" + Device + "\",\"sessionToken\":\"" + token + "\",\"messageType\":\"SENSOR_WINDOW\",\"payload\":{" +
        "\"activeMode\":\"" + mode + "\",\"detectorEvent\":\"" + detector + "\",\"detectorConfidence\":0.9,\"gyroscope\":" +
        (gyroscope ?? Gyroscope(plateau)) + "}}";

    private static async Task RulesDecideWithoutModel()
    {
        await using var h = new Harness(withModel: false);
        Check.True(!h.Recognizer.HasModel, "sin modelo");
        var payload = await h.Send(Window("PRESENTATION", "TWIST_OUT", 4.0));
        Check.Equal("EXECUTED", payload.GetProperty("status").GetString());
        Check.Equal("SLIDE_NEXT", payload.GetProperty("executedAction").GetString());
        Check.Equal(RecognitionInfo.ByRules, payload.GetProperty("recognizedBy").GetString());
        Check.Equal("TWIST_OUT", payload.GetProperty("gesture").GetString());
        Check.Equal(AllowedAction.SlideNext, h.Executor.Done.Single());
        Check.Equal(RecognitionInfo.ByRules, h.Events.Single().Recognition!.RecognizedBy);
        Check.True(h.Log.Lines.Any(l => l.Contains("by=RULES gesture=TWIST_OUT")), "el registro dice quién decidió");
    }

    private static async Task UnclearDoesNothingWithoutModel()
    {
        await using var h = new Harness(withModel: false);
        var payload = await h.Send(Window("PRESENTATION", "UNCLEAR", 4.0));
        Check.Equal("REJECTED_LOW_CONFIDENCE", payload.GetProperty("status").GetString());
        Check.Equal(0, h.Executor.Done.Count);
    }

    private static async Task ModelDecides()
    {
        await using var h = new Harness(withModel: true);
        // Las reglas dicen «hacia dentro», pero el movimiento es claramente hacia fuera: manda la IA.
        var payload = await h.Send(Window("MULTIMEDIA", "TWIST_IN", 4.0));
        Check.Equal("EXECUTED", payload.GetProperty("status").GetString());
        Check.Equal("MEDIA_NEXT_TRACK", payload.GetProperty("executedAction").GetString());
        Check.Equal(RecognitionInfo.ByModel, payload.GetProperty("recognizedBy").GetString());
        Check.Equal("TWIST_OUT", payload.GetProperty("gesture").GetString());
        Check.True(payload.GetProperty("confidence").GetDouble() >= 0.8, "confianza del modelo");
        Check.Equal(AllowedAction.MediaNextTrack, h.Executor.Done.Single());
    }

    private static async Task ModelRejectsNoGesture()
    {
        await using var h = new Harness(withModel: true);
        // Sin movimiento: el modelo sintético lo clasifica como «sin gesto».
        var payload = await h.Send(Window("PRESENTATION", "TWIST_OUT", 0.0));
        Check.Equal("REJECTED_LOW_CONFIDENCE", payload.GetProperty("status").GetString());
        Check.Equal(RecognitionReasons.NoGesture, payload.GetProperty("reasonCode").GetString());
        Check.Equal(RecognitionInfo.ByModel, payload.GetProperty("recognizedBy").GetString());
        Check.Equal(0, h.Executor.Done.Count);
    }

    private static async Task NeedsPairingAndRejectsReplay()
    {
        await using var h = new Harness(withModel: true);
        var stranger = await h.Send(Window("PRESENTATION", "TWIST_OUT", 4.0, token: "clave-falsa"));
        Check.Equal(ReasonCodes.UnauthorizedDevice, stranger.GetProperty("reasonCode").GetString());

        var id = Guid.NewGuid().ToString("D");
        var first = await h.Send(Window("PRESENTATION", "TWIST_OUT", 4.0, id));
        var again = await h.Send(Window("PRESENTATION", "TWIST_OUT", 4.0, id));
        Check.Equal("EXECUTED", first.GetProperty("status").GetString());
        Check.Equal(ReasonCodes.ReplayedMessage, again.GetProperty("reasonCode").GetString());
        Check.Equal(1, h.Executor.Done.Count, "solo la primera se ejecuta");
    }

    private static async Task MalformedWindowsAreRejected()
    {
        await using var h = new Harness(withModel: true);
        var cases = new[]
        {
            Window("PRESENTATION", "TWIST_OUT", 4.0).Replace("\"payload\":", "\"extra\":1,\"payload\":"),
            Window("PRESENTATION", "OTRO", 4.0),
            Window("PRESENTATION", "TWIST_OUT", 0, gyroscope: "[[0,1,2]]"),
            Window("PRESENTATION", "TWIST_OUT", 0, gyroscope: "[[0,0,0,0],[20,99,0,0]," + string.Join(",", Enumerable.Range(2, 10).Select(i => $"[{i * 20},0,0,0]")) + "]"),
            Window("PRESENTATION", "TWIST_OUT", 0, gyroscope: "[[0,0,0,0],[20,0,0,0]]"),
        };
        foreach (var message in cases)
        {
            var payload = await h.Send(message);
            Check.Equal("REJECTED_INVALID_MESSAGE", payload.GetProperty("status").GetString(), message[..Math.Min(80, message.Length)]);
        }
        Check.Equal(0, h.Executor.Done.Count);
    }
}
