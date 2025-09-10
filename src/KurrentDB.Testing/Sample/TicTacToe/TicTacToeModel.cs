// ReSharper disable CheckNamespace

namespace TicTacToe;

public enum Player {
    X, O
}

public enum GameStatus {
    Ongoing,
    Won,
    Draw
}

public record Position(int Row, int Column);

public record Cell(Position Position, CellState State);

public record Board(Cell[] Cells) {
    public static readonly Board Empty = CreateEmpty();

    public static Board CreateEmpty() {
        var cells = Enumerable.Range(0, 3)
            .SelectMany(row => Enumerable.Range(0, 3).Select(col => new Cell(new Position(row, col), CellState.Empty)))
            .ToArray();

        return new(cells);
    }
}

public enum CellState {
    Empty,
    X,
    O
}

// Commands
public record StartGame(Guid GameId, Player StartingPlayer);

public record MakeMove(Guid GameId, Position Position, Player Player);

// Events
public record GameStarted(Guid GameId, Player StartingPlayer);

public record MoveMade(Guid GameId, Position Position, Player Player);

public record GameWon(Guid GameId, Player Winner, Position[] WinningLine);

public record GameDraw(Guid GameId);
