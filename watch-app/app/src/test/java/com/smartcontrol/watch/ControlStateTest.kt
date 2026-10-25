package com.smartcontrol.watch

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

}
