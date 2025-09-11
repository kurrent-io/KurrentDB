// ReSharper disable CheckNamespace

using Shouldly;

namespace TicTacToe;

public class TicTacToeGameTests {
    [Test]
    public void game_can_be_started() {
        // Arrange
        var command = new StartGame(Guid.NewGuid(), Player.X);

        var expectedEvents = new object[] {
            new GameStarted(command.GameId, command.StartingPlayer)
        };

        var expectedGameState = new Game {
            Id            = command.GameId,
            CurrentPlayer = command.StartingPlayer,
            Status        = GameStatus.Ongoing,
            Board         = Board.Empty,
            Events        = expectedEvents.ToList()
        };

        var game = new Game();

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        events.ShouldHaveSingleItem();
        events.ShouldBeEquivalentTo(expectedEvents);
        actualGameState.ShouldBeEquivalentTo(expectedGameState);
    }

    [Test]
    public void move_is_made_and_player_turn_updates_when_move_is_valid() {
        // Arrange
        var game = new Game {
            Id            = Guid.NewGuid(),
            CurrentPlayer = Player.X,
            Status        = GameStatus.Ongoing,
            Board         = Board.CreateEmpty()
        };

        var move    = new Position(0, 0);
        var command = new MakeMove(game.Id, move, Player.X);

        var expectedEvents = new object[] {
            new MoveMade(game.Id, move, Player.X)
        };

        var expectedBoard = Board.CreateEmpty().Cells.Select(c => c.Position == move ? c with { State = CellState.X } : c).ToArray();
        var expectedGameState = game with {
            Board = new Board(expectedBoard),
            CurrentPlayer = Player.O,
            Events = [.. expectedEvents] // Include the expected events in the state
        };

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        events.ShouldHaveSingleItem();
        events.ShouldBeEquivalentTo(expectedEvents);
        actualGameState.ShouldBeEquivalentTo(expectedGameState);
    }

    [Test]
    public void multiple_moves_are_made_and_player_turns_alternate_correctly_when_moves_are_valid() {
        // Arrange
        var gameId = Guid.NewGuid();
        var game = new Game {
            Id            = gameId,
            CurrentPlayer = Player.X,
            Status        = GameStatus.Ongoing,
            Board         = Board.CreateEmpty()
        };

        var moveX    = new Position(0, 0);
        var commandX = new MakeMove(gameId, moveX, Player.X);

        // Act: Player X moves
        var (gameAfterX, eventsX) = game.Execute(commandX);

        // Assert: Player X's move
        eventsX.ShouldHaveSingleItem();
        var moveMadeEventX = eventsX.First().ShouldBeOfType<MoveMade>();
        moveMadeEventX.Player.ShouldBe(Player.X);
        gameAfterX.CurrentPlayer.ShouldBe(Player.O);
        gameAfterX.Board.Cells.First(c => c.Position == moveX).State.ShouldBe(CellState.X);

        // Arrange: Player O's turn
        var moveO    = new Position(1, 1);
        var commandO = new MakeMove(gameId, moveO, Player.O);

        // Act: Player O moves
        var (gameAfterO, eventsO) = gameAfterX.Execute(commandO);

        // Assert: Player O's move
        eventsO.ShouldHaveSingleItem();
        var moveMadeEventO = eventsO.First().ShouldBeOfType<MoveMade>();
        moveMadeEventO.Player.ShouldBe(Player.O);
        gameAfterO.CurrentPlayer.ShouldBe(Player.X);
        gameAfterO.Board.Cells.First(c => c.Position == moveO).State.ShouldBe(CellState.O);
    }

    [Test]
    public void invalid_operation_exception_is_thrown_when_move_is_on_occupied_cell() {
        // Arrange
        var gameId = Guid.NewGuid();
        var game   = new Game { Id = gameId, CurrentPlayer = Player.X, Status = GameStatus.Ongoing, Board = Board.CreateEmpty() };
        var move   = new Position(0, 0);

        // Player X makes a move
        (game, _) = game.Execute(new MakeMove(gameId, move, Player.X));

        // Player O attempts to move on the same cell
        var command = new MakeMove(gameId, move, Player.O);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => game.Execute(command))
            .Message.ShouldContain($"Invalid move: Cell at {move} is already occupied with {CellState.X}");
    }

    [Test]
    public void invalid_operation_exception_is_thrown_when_move_is_made_out_of_turn() {
        // Arrange
        var gameId  = Guid.NewGuid();
        var game    = new Game { Id = gameId, CurrentPlayer = Player.X, Status = GameStatus.Ongoing, Board = Board.CreateEmpty() };
        var command = new MakeMove(gameId, new Position(0, 0), Player.O); // Player O tries to move when it's X's turn

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => game.Execute(command))
            .Message.ShouldBe("It's not your turn Player O");
    }

    [Test]
    public void game_is_won_and_game_won_event_is_raised_when_player_x_completes_first_row() {
        // Arrange
        var gameId = Guid.NewGuid();

        // Correctly initialize boardCells with the desired state using 'with' expressions
        var boardCells = Board.CreateEmpty().Cells.Select(c => {
                if (c.Position == new Position(0, 0)) return c with { State = CellState.X };
                if (c.Position == new Position(0, 1)) return c with { State = CellState.X };
                if (c.Position == new Position(1, 0)) return c with { State = CellState.O }; // Simulate O's moves
                if (c.Position == new Position(1, 1)) return c with { State = CellState.O }; // Simulate O's moves

                return c; // Other cells remain Empty
            }
        ).ToArray();

        var game = new Game {
            Id            = gameId,
            CurrentPlayer = Player.X,
            Status        = GameStatus.Ongoing,
            Board         = new Board(boardCells) // Use the correctly prepared board
        };

        var winningMove = new Position(0, 2);
        var command     = new MakeMove(gameId, winningMove, Player.X);

        var expectedWinningLine = new[] { new Position(0, 0), new Position(0, 1), new Position(0, 2) };

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        events.Length.ShouldBe(2);
        var moveMadeEvent = events[0].ShouldBeOfType<MoveMade>();
        moveMadeEvent.Position.ShouldBe(winningMove);
        var gameWonEvent = events[1].ShouldBeOfType<GameWon>();
        gameWonEvent.Winner.ShouldBe(Player.X);
        gameWonEvent.WinningLine.ShouldBeEquivalentTo(expectedWinningLine);

        actualGameState.Status.ShouldBe(GameStatus.Won);
        actualGameState.Winner.ShouldBe(Player.X);
    }

    [Test]
    public void game_is_won_and_game_won_event_is_raised_when_player_o_completes_second_column() {
        // Arrange
        var gameId = Guid.NewGuid();
        var initialCells = new List<Cell> {
            new(new Position(0, 0), CellState.X), new(new Position(0, 1), CellState.O), new(new Position(0, 2), CellState.X),
            new(new Position(1, 0), CellState.Empty), new(new Position(1, 1), CellState.Empty), new(new Position(1, 2), CellState.Empty),
            new(new Position(2, 0), CellState.X), new(new Position(2, 1), CellState.O), new(new Position(2, 2), CellState.Empty)
        };

        var boardCells = Board.CreateEmpty().Cells.Select(c => {
                var initial = initialCells.FirstOrDefault(ic => ic.Position == c.Position);
                return initial ?? c;
            }
        ).ToArray();

        var game = new Game {
            Id            = gameId,
            CurrentPlayer = Player.O,
            Status        = GameStatus.Ongoing,
            Board         = new Board(boardCells)
        };

        var winningMove         = new Position(1, 1);
        var command             = new MakeMove(gameId, winningMove, Player.O);
        var expectedWinningLine = new[] { new Position(0, 1), new Position(1, 1), new Position(2, 1) };

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        events.Length.ShouldBe(2);
        var moveMadeEvent = events[0].ShouldBeOfType<MoveMade>();
        moveMadeEvent.Position.ShouldBe(winningMove);
        var gameWonEvent = events[1].ShouldBeOfType<GameWon>();
        gameWonEvent.Winner.ShouldBe(Player.O);
        gameWonEvent.WinningLine.ShouldBeEquivalentTo(expectedWinningLine);
        actualGameState.Status.ShouldBe(GameStatus.Won);
        actualGameState.Winner.ShouldBe(Player.O);
    }

    [Test]
    public void game_is_won_and_game_won_event_is_raised_when_player_x_completes_main_diagonal() {
        // Arrange
        var gameId = Guid.NewGuid();
        var initialCells = new List<Cell> {
            new(new Position(0, 0), CellState.X), new(new Position(0, 1), CellState.O), new(new Position(0, 2), CellState.Empty),
            new(new Position(1, 0), CellState.O), new(new Position(1, 1), CellState.X), new(new Position(1, 2), CellState.Empty),
            new(new Position(2, 0), CellState.Empty), new(new Position(2, 1), CellState.Empty), new(new Position(2, 2), CellState.Empty)
        };

        var boardCells          = Board.CreateEmpty().Cells.Select(c => initialCells.FirstOrDefault(ic => ic.Position == c.Position) ?? c).ToArray();
        var game                = new Game { Id = gameId, CurrentPlayer = Player.X, Status = GameStatus.Ongoing, Board = new Board(boardCells) };
        var winningMove         = new Position(2, 2);
        var command             = new MakeMove(gameId, winningMove, Player.X);
        var expectedWinningLine = new[] { new Position(0, 0), new Position(1, 1), new Position(2, 2) };

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        var gameWonEvent = events.OfType<GameWon>().Single();
        gameWonEvent.Winner.ShouldBe(Player.X);
        gameWonEvent.WinningLine.ShouldBeEquivalentTo(expectedWinningLine);
        actualGameState.Status.ShouldBe(GameStatus.Won);
    }

    [Test]
    public void game_is_won_and_game_won_event_is_raised_when_player_o_completes_anti_diagonal() {
        // Arrange
        var gameId = Guid.NewGuid();
        var initialCells = new List<Cell> {
            new(new Position(0, 0), CellState.X), new(new Position(0, 1), CellState.X), new(new Position(0, 2), CellState.O),
            new(new Position(1, 0), CellState.Empty), new(new Position(1, 1), CellState.O), new(new Position(1, 2), CellState.X),
            new(new Position(2, 0), CellState.Empty), new(new Position(2, 1), CellState.Empty), new(new Position(2, 2), CellState.Empty)
        };

        var boardCells          = Board.CreateEmpty().Cells.Select(c => initialCells.FirstOrDefault(ic => ic.Position == c.Position) ?? c).ToArray();
        var game                = new Game { Id = gameId, CurrentPlayer = Player.O, Status = GameStatus.Ongoing, Board = new Board(boardCells) };
        var winningMove         = new Position(2, 0);
        var command             = new MakeMove(gameId, winningMove, Player.O);
        var expectedWinningLine = new[] { new Position(0, 2), new Position(1, 1), new Position(2, 0) };

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        var gameWonEvent = events.OfType<GameWon>().Single();
        gameWonEvent.Winner.ShouldBe(Player.O);
        gameWonEvent.WinningLine.ShouldBeEquivalentTo(expectedWinningLine);
        actualGameState.Status.ShouldBe(GameStatus.Won);
    }

    [Test]
    public void game_is_draw_and_game_draw_event_is_raised_when_board_is_full_and_no_winner() {
        // Arrange
        var gameId = Guid.NewGuid();
        var cells = new[] {
            new Cell(new Position(0, 0), CellState.X), new Cell(new Position(0, 1), CellState.O), new Cell(new Position(0, 2), CellState.X),
            new Cell(new Position(1, 0), CellState.X), new Cell(new Position(1, 1), CellState.X), new Cell(new Position(1, 2), CellState.O),
            new Cell(new Position(2, 0), CellState.O), new Cell(new Position(2, 1), CellState.Empty), new Cell(new Position(2, 2), CellState.O)
        };

        var game      = new Game { Id = gameId, CurrentPlayer = Player.X, Status = GameStatus.Ongoing, Board = new Board(cells) };
        var finalMove = new Position(2, 1);
        var command   = new MakeMove(gameId, finalMove, Player.X);

        // Act
        var (actualGameState, events) = game.Execute(command);

        // Assert
        events.Length.ShouldBe(2);
        events[0].ShouldBeOfType<MoveMade>();
        var gameDrawEvent = events[1].ShouldBeOfType<GameDraw>();
        gameDrawEvent.GameId.ShouldBe(gameId);
        actualGameState.Status.ShouldBe(GameStatus.Draw);
        actualGameState.Winner.ShouldBeNull();
    }

    [Test]
    public void invalid_operation_exception_is_thrown_when_move_is_made_after_game_is_won() {
        // Arrange
        var gameId  = Guid.NewGuid();
        var game    = new Game { Id = gameId, CurrentPlayer = Player.X, Status = GameStatus.Won, Winner = Player.X, Board = Board.CreateEmpty() };
        var command = new MakeMove(gameId, new Position(2, 2), Player.O);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => game.Execute(command))
            .Message.ShouldBe("Invalid command"); // Game logic prevents moves when status is not Ongoing
    }

    [Test]
    public void invalid_operation_exception_is_thrown_when_move_is_made_after_game_is_draw() {
        // Arrange
        var gameId  = Guid.NewGuid();
        var game    = new Game { Id = gameId, CurrentPlayer = Player.X, Status = GameStatus.Draw, Board = Board.CreateEmpty() };
        var command = new MakeMove(gameId, new Position(2, 2), Player.O);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => game.Execute(command))
            .Message.ShouldBe("Invalid command"); // Game logic prevents moves when status is not Ongoing
    }

    [Test]
    public void invalid_operation_exception_is_thrown_for_unrecognized_command_object() {
        // Arrange
        var game    = new Game();
        var command = new object(); // Unrecognized command

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => game.Execute(command))
            .Message.ShouldBe("Invalid command");
    }
}
