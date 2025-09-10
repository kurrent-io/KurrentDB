using TicTacToe;

namespace KurrentDB.Testing.Sample.TicTacToe;

public static class TicTacToeSimulator {
    /// <summary>
    /// Simulates a complete Tic Tac Toe game from start to finish (win or draw).
    /// The starting player is chosen randomly.
    /// Players make moves by picking the first available empty cell.
    /// </summary>
    /// <returns>A tuple containing the game ID, list of game events, and the winner (null if draw)</returns>
    public static (Guid GameId, List<object> Events, Player? Winner) SimulateGame() {
        var gameId = Guid.NewGuid();
        var game = new Game();

        // 1. Start the game with a random starting player
        var startingPlayer = Random.Shared.Next(0, 2) == 0 ? Player.X : Player.O;
        var startGameCmd = new StartGame(gameId, startingPlayer);
        (game, _) = game.Execute(startGameCmd);

        // 2. Make moves until the game ends
        while (game.Status == GameStatus.Ongoing) {
            var currentPlayer = game.CurrentPlayer;

            // Find the first available empty cell for the current player's move
            var availableCell = game.Board.Cells.FirstOrDefault(c => c.State == CellState.Empty);

            if (availableCell == null) {
                // This shouldn't happen if game logic is working correctly,
                // but added as a safeguard
                break;
            }

            var makeMoveCmd = new MakeMove(game.Id, availableCell.Position, currentPlayer);
            (game, _) = game.Execute(makeMoveCmd);
        }

        // Return the final game state information
        return (game.Id, game.Events, game.Winner);
    }
}
