"""Ejecutor SIMULADO de la PoC 1.

Representa la frontera del futuro módulo windows-app/executor: solo acepta un
valor del enum cerrado AllowedAction, nunca texto procedente de la red. No envía
teclas, no llama a APIs de Windows y no inicia procesos; únicamente registra qué
acción se habría ejecutado.
"""

from __future__ import annotations

from typing import List

from validator import AllowedAction

# Mapeo estático. En la aplicación definitiva (C#/.NET) estas entradas serán las
# llamadas nativas predefinidas descritas en docs/security-allowlist.md.
_SIMULATED_EFFECTS = {
    AllowedAction.SLIDE_NEXT: "simulado: avanzar diapositiva",
    AllowedAction.SLIDE_PREVIOUS: "simulado: retroceder diapositiva",
    AllowedAction.MEDIA_PLAY: "simulado: reproducir (acción explícita, no alternador)",
    AllowedAction.MEDIA_PAUSE: "simulado: pausar (acción explícita, no alternador)",
}


class SimulatedExecutor:
    def __init__(self):
        self.history: List[AllowedAction] = []

    def execute(self, action: AllowedAction) -> str:
        if type(action) is not AllowedAction:
            raise TypeError("el ejecutor solo acepta AllowedAction")
        self.history.append(action)
        return _SIMULATED_EFFECTS[action]
