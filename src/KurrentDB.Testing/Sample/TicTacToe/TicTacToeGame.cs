// ReSharper disable CheckNamespace

namespace TicTacToe;

public record Game {
    public Guid         Id            { get; init; }
    public Board        Board         { get; init; } = Board.CreateEmpty();
    public Player       CurrentPlayer { get; init; }
    public GameStatus   Status        { get; init; } = GameStatus.Ongoing;
    public Player?      Winner        { get; init; }
    public List<object> Events        { get; init; } = [];

    public (Game UpdatedGame, object[] Events) Execute(object command) {
        var result = command switch {
            StartGame cmd                                  => StartNewGame(cmd),
            MakeMove cmd when Status == GameStatus.Ongoing => MakePlayerMove(cmd),
            _                                              => throw new InvalidOperationException("Invalid command")
        };

        // Track events in the game state
        var (game, events) = result;
        var updatedEvents = new List<object>(game.Events);
        updatedEvents.AddRange(events);

        return (game with { Events = updatedEvents }, events);
    }

    (Game, object[]) StartNewGame(StartGame cmd) {
        var gameStartedEvent = new GameStarted(cmd.GameId, cmd.StartingPlayer);
        return (this with { Id = cmd.GameId, CurrentPlayer = cmd.StartingPlayer }, [gameStartedEvent]);
    }

    (Game, object[]) MakePlayerMove(MakeMove cmd) {
        if (cmd.Player != CurrentPlayer)
            throw new InvalidOperationException($"It's not your turn Player {cmd.Player}");

        var cellToUpdate = Board.Cells.FirstOrDefault(c => c.Position == cmd.Position);
        if (cellToUpdate == null) {
            // This should ideally not happen if Position is always within bounds,
            // but good for robustness if Position could be invalid.
            throw new InvalidOperationException($"Invalid move: Position {cmd.Position} is not on the board.");
        }

        if (cellToUpdate.State != CellState.Empty)
            throw new InvalidOperationException(
                $"Invalid move: Cell at {cmd.Position} is already occupied with {cellToUpdate.State}");

        CellState newCellState = cmd.Player switch {
            Player.X => CellState.X,
            Player.O => CellState.O,
            _        => throw new ArgumentOutOfRangeException(nameof(cmd.Player), "Invalid player value")
        };

        var newBoard = new Board(Board.Cells.Select(c => c.Position == cmd.Position ? c with { State = newCellState } : c).ToArray());

        var events  = new List<object> { new MoveMade(Id, cmd.Position, cmd.Player) };
        var newGame = this with { Board = newBoard };

        if (CheckWin(newBoard, cmd.Player, out var winningLine)) {
            events.Add(new GameWon(Id, cmd.Player, winningLine));
            return (newGame with { Status = GameStatus.Won, Winner = cmd.Player }, events.ToArray());
        }

        if (newBoard.Cells.All(c => c.State != CellState.Empty)) {
            events.Add(new GameDraw(Id));
            return (newGame with { Status = GameStatus.Draw }, events.ToArray());
        }

        return (newGame with { CurrentPlayer = cmd.Player == Player.X ? Player.O : Player.X }, events.ToArray());
    }

    public static bool CheckWin(Board board, Player player, out Position[] winningLine) {
        var playerMark = player switch {
            Player.X => CellState.X,
            Player.O => CellState.O,
            _        => throw new ArgumentOutOfRangeException(nameof(player), "Invalid player value for win check")
        };

        // Check rows
        for (var row = 0; row < 3; row++) {
            var rowCells = board.Cells.Where(c => c.Position.Row == row).ToArray();
            if (rowCells.All(c => c.State == playerMark)) {
                winningLine = rowCells.Select(c => c.Position).ToArray();
                return true;
            }
        }

        // Check columns
        for (var col = 0; col < 3; col++) {
            var colCells = board.Cells.Where(c => c.Position.Column == col).ToArray();
            if (colCells.All(c => c.State == playerMark)) {
                winningLine = colCells.Select(c => c.Position).ToArray();
                return true;
            }
        }

        // Check main diagonal
        var mainDiag = board.Cells.Where(c => c.Position.Row == c.Position.Column).ToArray();
        if (mainDiag.All(c => c.State == playerMark)) {
            winningLine = mainDiag.Select(c => c.Position).ToArray();
            return true;
        }

        // Check anti-diagonal
        var antiDiag = board.Cells.Where(c => c.Position.Row + c.Position.Column == 2).ToArray();
        if (antiDiag.All(c => c.State == playerMark)) {
            winningLine = antiDiag.Select(c => c.Position).ToArray();
            return true;
        }

        winningLine = [];
        return false;
    }
}
