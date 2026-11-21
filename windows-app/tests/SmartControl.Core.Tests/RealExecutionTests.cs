using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SmartControl.Core.Execution;
using SmartControl.Core.Logging;
using SmartControl.Core.Protocol;
using SmartControl.Core.Transport;

namespace SmartControl.Core.Tests;

/// <summary>
/// Respuestas del receptor cuando el ejecutor actúa de verdad. El ejecutor de la prueba es falso:
/// no pulsa teclas ni toca la música, solo dice que lo haría o falla con un motivo.
/// </summary>
internal static class RealExecutionTests
{
    private const string Device = "wearos-watch-01";
    // Valor ficticio solo para pruebas; no es una credencial real.
    private const string Token = "real-execution-test-token-not-a-secret";

    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("ejecución real: responde EXECUTED con la acción realizada", ExecutedCarriesAction);
        yield return ("ejecución real: un motivo conocido llega al reloj", KnownFailureKeepsReason);
        yield return ("ejecución real: un fallo inesperado es EXECUTOR_FAILED", UnexpectedFailureIsGeneric);
        yield return ("ejecución real: un comando rechazado no se ejecuta", RejectedNeverExecutes);
    }

    private sealed class FakeExecutor : ICommandExecutor
    {
        public Func<AllowedAction, string> Behaviour { get; set; } = a => $"hecho {a}";
        public List<AllowedAction> Calls { get; } = new();
        public bool PerformsRealActions => true;

        public string Execute(AllowedAction action)
        {
            lock (Calls) Calls.Add(action);
            return Behaviour(action);
        }
    }

    private static async Task<(JsonElement Payload, InMemoryEventLog Log)> Send(FakeExecutor executor, string mode, string action, double confidence = 0.95)
    {
        var log = new InMemoryEventLog();
        await using var receiver = new LocalCommandReceiver(
            new CommandValidator(new Dictionary<string, string> { [Device] = Token }), executor, log, port: 0);
        receiver.Start();
        var message = new CommandRequest { DeviceId = Device, SessionToken = Token, ActiveMode = mode, RequestedAction = action, Confidence = confidence }.ToUtf8Json();
        var response = await LoopbackTestClient.SendAsync(receiver.Port, message, TimeSpan.FromSeconds(5));
        return (JsonDocument.Parse(response).RootElement.GetProperty("payload").Clone(), log);
    }

    private static async Task ExecutedCarriesAction()
    {
        var executor = new FakeExecutor();
        var (payload, log) = await Send(executor, "PRESENTATION", "SLIDE_PREVIOUS");
        Check.Equal(CommandResponseWriter.Executed, payload.GetProperty("status").GetString());
        Check.Equal("SLIDE_PREVIOUS", payload.GetProperty("executedAction").GetString());
        Check.Equal("OK", payload.GetProperty("reasonCode").GetString());
        Check.Equal(AllowedAction.SlidePrevious, executor.Calls.Single());
        Check.True(log.Lines.Any(l => l.Contains("status=EXECUTED reason=OK action=SLIDE_PREVIOUS")), "el registro debe decir EXECUTED");
    }

    private static async Task KnownFailureKeepsReason()
    {
        var executor = new FakeExecutor { Behaviour = _ => throw new ActionUnavailableException(ReasonCodes.NoMediaSession) };
        var (payload, _) = await Send(executor, "MULTIMEDIA", "MEDIA_PAUSE");
        Check.Equal("EXECUTION_ERROR", payload.GetProperty("status").GetString());
        Check.Equal(ReasonCodes.NoMediaSession, payload.GetProperty("reasonCode").GetString());
        Check.Equal(JsonValueKind.Null, payload.GetProperty("executedAction").ValueKind);
    }

    private static async Task UnexpectedFailureIsGeneric()
    {
        var executor = new FakeExecutor { Behaviour = _ => throw new InvalidOperationException("detalle interno que no debe salir") };
        var (payload, log) = await Send(executor, "MULTIMEDIA", "MEDIA_PLAY");
        Check.Equal("EXECUTION_ERROR", payload.GetProperty("status").GetString());
        Check.Equal(ReasonCodes.ExecutorFailed, payload.GetProperty("reasonCode").GetString());
        Check.True(!payload.ToString().Contains("detalle interno"), "el detalle de la excepción no debe llegar al reloj");
        Check.True(!log.Lines.Any(l => l.Contains("detalle interno")), "ni al registro");
    }

    private static async Task RejectedNeverExecutes()
    {
        var executor = new FakeExecutor();
        var (lowConfidence, _) = await Send(executor, "PRESENTATION", "SLIDE_NEXT", confidence: 0.5);
        var (wrongMode, _) = await Send(executor, "PRESENTATION", "MEDIA_PLAY");
        Check.Equal("REJECTED_LOW_CONFIDENCE", lowConfidence.GetProperty("status").GetString());
        Check.Equal("REJECTED_NOT_ALLOWED", wrongMode.GetProperty("status").GetString());
        Check.Equal(0, executor.Calls.Count, "ningún rechazo llega al ejecutor");
    }
}
