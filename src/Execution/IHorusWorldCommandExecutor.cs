using System;
using HorusMod.Commands;

namespace HorusMod.Execution
{
    public interface IHorusWorldCommandExecutor
    {
        HorusCommandResult ExecuteSpawnUnit(SpawnUnitCommand command);
        // Stubs for future group, delete, factory, economy commands
        HorusCommandResult ExecuteCommand(HorusCommandRequest request);
    }
}
