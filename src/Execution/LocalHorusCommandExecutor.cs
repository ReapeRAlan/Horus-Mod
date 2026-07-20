using System;
using HorusMod.Commands;
using UnityEngine;

namespace HorusMod.Execution
{
    public class LocalHorusCommandExecutor : IHorusWorldCommandExecutor
    {
        public HorusCommandResult ExecuteSpawnUnit(SpawnUnitCommand command)
        {
            // v1.3.0 architecture stub — not wired to actual spawn logic yet.
            // Spawning currently happens directly via HorusManager/Spawner.i.
            return new HorusCommandResult { success = false, message = "Command executor spawn is a v1.3.0 architecture stub, not wired yet." };
        }

        public HorusCommandResult ExecuteCommand(HorusCommandRequest request)
        {
            switch (request.commandType)
            {
                case HorusCommandType.SpawnUnit:
                    var spawnCmd = UnityEngine.JsonUtility.FromJson<SpawnUnitCommand>(request.payloadJson);
                    return ExecuteSpawnUnit(spawnCmd);
                default:
                    return new HorusCommandResult { success = false, message = "Command type not implemented" };
            }
        }
    }
}
