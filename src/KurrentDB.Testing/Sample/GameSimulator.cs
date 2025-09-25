using KurrentDB.Testing.Sample.TicTacToe;

namespace KurrentDB.Testing.Sample;

public static class GameSimulator {
    public static (Guid GameId, List<object> GameEvents) Simulate(GamesAvailable game) {
        if (game == GamesAvailable.TicTacToe) {
            var result = TicTacToeSimulator.SimulateGame();
            return (result.GameId, result.Events);
        }

        throw new NotSupportedException($"The game '{game}' is not supported for simulation.");
    }
}
