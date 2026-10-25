package com.smartcontrol.watch

import com.smartcontrol.watch.gesture.GestureEvent
import com.smartcontrol.watch.gesture.UnclearReason
import com.smartcontrol.watch.gesture.WristGesture
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
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
        assertEquals(listOf(Command.MEDIA_PLAY, Command.MEDIA_PAUSE), state.availableCommands)
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
        assertEquals(Command.MEDIA_PLAY, commandFor(ControlMode.MULTIMEDIA, WristGesture.TWIST_OUT))
        assertEquals(Command.MEDIA_PAUSE, commandFor(ControlMode.MULTIMEDIA, WristGesture.TWIST_IN))
        // Cada gesto produce siempre un comando de la lista blanca de su modo.
        for (mode in ControlMode.entries) for (gesture in WristGesture.entries) {
            assertEquals(mode, commandFor(mode, gesture).mode)
        }
    }

    @Test
    fun recognizedGestureGoesThroughValidation() {
        val state = ControlState().selectMode(ControlMode.MULTIMEDIA)
            .onGesture(GestureEvent.Recognized(WristGesture.TWIST_IN, 0.95f))
        assertEquals(CommandResult.Simulated(Command.MEDIA_PAUSE, 1), state.lastResult)
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

}
