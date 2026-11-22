package com.smartcontrol.watch

import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.MIN_GESTURE_CONFIDENCE
import com.smartcontrol.watch.gesture.UnclearReason
import com.smartcontrol.watch.gesture.WristGesture
import com.smartcontrol.watch.link.CommandOutcome
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class ControlStateTest {

    @Test
    fun startsInPresentationModeDisconnectedWithSimulation() {
        val state = ControlState()
        assertEquals(ControlMode.PRESENTATION, state.mode)
        assertEquals(ConnectionStatus.DISCONNECTED, state.connection)
        assertEquals(true, state.simulationEnabled)
        assertEquals(listOf(Command.SLIDE_NEXT, Command.SLIDE_PREVIOUS), state.availableCommands)
    }

    @Test
    fun multimediaModeOffersOnlyMediaCommands() {
        val state = ControlState().selectMode(ControlMode.MULTIMEDIA)
        assertEquals(
            listOf(Command.MEDIA_PLAY, Command.MEDIA_PAUSE, Command.MEDIA_NEXT_TRACK, Command.MEDIA_PREVIOUS_TRACK),
            state.availableCommands,
        )
    }

    @Test
    fun commandIsSimulatedWhenSimulationIsOn() {
        val state = ControlState()
            .sendCommand(Command.SLIDE_NEXT)
            .sendCommand(Command.SLIDE_PREVIOUS)
        assertEquals(CommandResult.Simulated(Command.SLIDE_PREVIOUS, 2), state.lastResult)
        assertEquals(2, state.sentCount)
    }

    @Test
    fun simulationDoesNotMarkAsConnected() {
        val state = ControlState().sendCommand(Command.SLIDE_NEXT)
        assertEquals(ConnectionStatus.DISCONNECTED, state.connection)
    }

    @Test
    fun commandIsRejectedWithoutConnectionWhenSimulationIsOff() {
        val state = ControlState().setSimulation(false).sendCommand(Command.SLIDE_NEXT)
        assertEquals(CommandResult.Rejected(Command.SLIDE_NEXT, RejectReason.NO_CONNECTION), state.lastResult)
        assertEquals(0, state.sentCount)
    }

    @Test
    fun commandFromOtherModeIsRejectedEvenInSimulation() {
        val state = ControlState().sendCommand(Command.MEDIA_PLAY)
        assertEquals(CommandResult.Rejected(Command.MEDIA_PLAY, RejectReason.WRONG_MODE), state.lastResult)
        assertEquals(0, state.sentCount)
    }

    @Test
    fun modeIsCheckedBeforeConnection() {
        val state = ControlState().setSimulation(false).sendCommand(Command.MEDIA_PAUSE)
        assertEquals(CommandResult.Rejected(Command.MEDIA_PAUSE, RejectReason.WRONG_MODE), state.lastResult)
    }

    @Test
    fun changingModeClearsLastResultButKeepsCount() {
        val state = ControlState()
            .sendCommand(Command.SLIDE_NEXT)
            .selectMode(ControlMode.MULTIMEDIA)
        assertNull(state.lastResult)
        assertEquals(1, state.sentCount)
    }

    @Test
    fun changingSimulationClearsLastResult() {
        val state = ControlState().sendCommand(Command.SLIDE_NEXT).setSimulation(false)
        assertNull(state.lastResult)
    }

    @Test
    fun selectingSameModeOrSimulationKeepsState() {
        val state = ControlState().sendCommand(Command.SLIDE_NEXT)
        assertSame(state, state.selectMode(ControlMode.PRESENTATION))
        assertSame(state, state.setSimulation(true))
    }

    @Test
    fun gesturesMapToTheActiveModeCommands() {
        assertEquals(Command.SLIDE_NEXT, commandFor(ControlMode.PRESENTATION, WristGesture.TWIST_OUT))
        assertEquals(Command.SLIDE_PREVIOUS, commandFor(ControlMode.PRESENTATION, WristGesture.TWIST_IN))
        assertEquals(Command.MEDIA_NEXT_TRACK, commandFor(ControlMode.MULTIMEDIA, WristGesture.TWIST_OUT))
        assertEquals(Command.MEDIA_PREVIOUS_TRACK, commandFor(ControlMode.MULTIMEDIA, WristGesture.TWIST_IN))
        // Cada gesto produce siempre un comando de la lista blanca de su modo.
        for (mode in ControlMode.entries) for (gesture in WristGesture.entries) {
            assertEquals(mode, commandFor(mode, gesture).mode)
        }
    }

    @Test
    fun recognizedGestureGoesThroughValidation() {
        val state = ControlState().selectMode(ControlMode.MULTIMEDIA)
            .onGesture(GestureEvent.Recognized(WristGesture.TWIST_IN, 0.95f))
        assertEquals(CommandResult.Simulated(Command.MEDIA_PREVIOUS_TRACK, 1), state.lastResult)
    }

    @Test
    fun recognizedGestureIsRejectedWithoutConnection() {
        val state = ControlState().setSimulation(false)
            .onGesture(GestureEvent.Recognized(WristGesture.TWIST_OUT, 0.95f))
        assertEquals(CommandResult.Rejected(Command.SLIDE_NEXT, RejectReason.NO_CONNECTION), state.lastResult)
        assertEquals(0, state.sentCount)
    }

    @Test
    fun unclearGestureSendsNothing() {
        val state = ControlState().onGesture(GestureEvent.Unclear(0.6f, UnclearReason.WEAK))
        assertEquals(CommandResult.GestureUnrecognized(0.6f, UnclearReason.WEAK), state.lastResult)
        assertEquals(0, state.sentCount)
    }

    @Test
    fun gestureBelowConfidenceThresholdSendsNothing() {
        val state = ControlState().onGesture(GestureEvent.Recognized(WristGesture.TWIST_OUT, 0.79f))
        assertEquals(CommandResult.GestureUnrecognized(0.79f, UnclearReason.WEAK), state.lastResult)
        assertEquals(0, state.sentCount)
    }

    @Test
    fun gestureAtExactThresholdIsAccepted() {
        val state = ControlState().onGesture(GestureEvent.Recognized(WristGesture.TWIST_OUT, MIN_GESTURE_CONFIDENCE))
        assertEquals(CommandResult.Simulated(Command.SLIDE_NEXT, 1), state.lastResult)
    }

    @Test
    fun thresholdIsTheProtocolValue() {
        assertEquals(0.80f, MIN_GESTURE_CONFIDENCE, 0f)
    }

    private fun connected() = ControlState(simulationEnabled = false).setConnection(ConnectionStatus.CONNECTED)

    @Test
    fun withConnectionTheCommandWaitsForWindows() {
        val state = connected().sendCommand(Command.SLIDE_NEXT)
        assertEquals(PendingCommand(Command.SLIDE_NEXT, 1), state.pending)
        assertNull("no se muestra nada hasta que responda Windows", state.lastResult)
    }

    @Test
    fun windowsAnswerBecomesTheResult() {
        val sent = connected().sendCommand(Command.SLIDE_PREVIOUS)
        val done = sent.complete(1, CommandOutcome.Executed())
        assertEquals(CommandResult.Executed(Command.SLIDE_PREVIOUS, 1), done.lastResult)
        assertNull(done.pending)

        val failed = connected().selectMode(ControlMode.MULTIMEDIA).sendCommand(Command.MEDIA_PLAY)
            .complete(1, CommandOutcome.Failed("NO_MEDIA_SESSION"))
        val result = failed.lastResult as CommandResult.Failed
        assertEquals(R.string.fail_no_media, result.failure.label)
        assertEquals("NO_MEDIA_SESSION", result.failure.code)

        val silent = connected().sendCommand(Command.SLIDE_NEXT).complete(1, CommandOutcome.NoResponse)
        assertEquals(R.string.fail_no_response, (silent.lastResult as CommandResult.Failed).failure.label)
    }

    @Test
    fun oneCommandAtATime() {
        val sent = connected().sendCommand(Command.SLIDE_NEXT)
        assertSame("mientras espera, otro gesto no envía nada", sent, sent.sendCommand(Command.SLIDE_PREVIOUS))
        assertSame("una respuesta de otro comando se ignora", sent, sent.complete(7, CommandOutcome.Executed()))
    }

    @Test
    fun modeIsStillCheckedBeforeSending() {
        val state = connected().sendCommand(Command.MEDIA_PLAY)
        assertNull(state.pending)
        assertEquals(CommandResult.Rejected(Command.MEDIA_PLAY, RejectReason.WRONG_MODE), state.lastResult)
    }

    @Test
    fun withoutConnectionNothingIsSent() {
        val state = ControlState(simulationEnabled = false).sendCommand(Command.SLIDE_NEXT)
        assertNull(state.pending)
        assertEquals(CommandResult.Rejected(Command.SLIDE_NEXT, RejectReason.NO_CONNECTION), state.lastResult)
    }

    @Test
    fun tappedCommandsMapBackToTheirGesture() {
        for (mode in ControlMode.entries) for (gesture in WristGesture.entries) {
            assertEquals(gesture, gestureFor(commandFor(mode, gesture)))
        }
        assertTrue(Command.entries.all { command -> gestureFor(command)?.let { commandFor(command.mode, it) == command } ?: true })
    }

    @Test
    fun withConnectionAMovementIsDecidedByWindows() {
        val sent = connected().selectMode(ControlMode.MULTIMEDIA)
            .sendMovement(GestureEvent.Recognized(WristGesture.TWIST_IN, 0.9f))
        assertEquals("la acción la decide Windows", PendingCommand(null, 1), sent.pending)

        val byAi = sent.complete(1, CommandOutcome.Executed("MEDIA_NEXT_TRACK", "AI", 0.93))
        assertEquals(CommandResult.Executed(Command.MEDIA_NEXT_TRACK, 1, Recognition(true, 0.93)), byAi.lastResult)

        val ignored = sent.complete(1, CommandOutcome.Failed("NO_GESTURE", "AI"))
        assertEquals(R.string.fail_ai_no_gesture, (ignored.lastResult as CommandResult.MovementIgnored).failure.label)

        val refused = sent.complete(1, CommandOutcome.Failed("NO_MEDIA_SESSION", "RULES"))
        assertEquals(CommandResult.Failed(null, RemoteFailure("NO_MEDIA_SESSION", R.string.fail_no_media)), refused.lastResult)
    }

    @Test
    fun withoutConnectionAMovementIsDecidedOnTheWatch() {
        val state = ControlState(simulationEnabled = false).sendMovement(GestureEvent.Recognized(WristGesture.TWIST_OUT, 0.9f))
        assertNull(state.pending)
        assertEquals(CommandResult.Rejected(Command.SLIDE_NEXT, RejectReason.NO_CONNECTION), state.lastResult)
    }

    @Test
    fun playAndPauseAreScreenButtonsNotGestures() {
        assertNull(gestureFor(Command.MEDIA_PLAY))
        assertNull(gestureFor(Command.MEDIA_PAUSE))
        val state = connected().selectMode(ControlMode.MULTIMEDIA)
        assertEquals(PendingCommand(Command.MEDIA_PLAY, 1), state.sendCommand(Command.MEDIA_PLAY).pending)
        assertEquals(PendingCommand(Command.MEDIA_PAUSE, 1), state.sendCommand(Command.MEDIA_PAUSE).pending)
    }
}
