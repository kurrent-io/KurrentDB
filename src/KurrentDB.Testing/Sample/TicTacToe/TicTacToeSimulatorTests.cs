// ReSharper disable CheckNamespace

using KurrentDB.Testing.Sample.TicTacToe;
using Shouldly;

namespace TicTacToe;

public class TicTacToeSimulatorTests {
    [Test]
    public void generates_a_non_empty_list_of_events_for_a_simulated_game() {
        // Act
        var (_, events, _) = TicTacToeSimulator.SimulateGame();

        // Assert
        events.ShouldNotBeEmpty();
    }

    [Test]
    public void first_event_is_always_game_started_with_valid_details() {
        // Act
        var (gameId, events, _) = TicTacToeSimulator.SimulateGame();

        // Assert
        var gameStartedEvent = events.First().ShouldBeOfType<GameStarted>();
        gameStartedEvent.GameId.ShouldBe(gameId);
        gameStartedEvent.GameId.ShouldNotBe(Guid.Empty);
        (gameStartedEvent.StartingPlayer is Player.X or Player.O).ShouldBeTrue();
    }

    [Test]
    public void last_event_is_always_game_won_or_game_draw() {
        // Act
        var (_, events, _) = TicTacToeSimulator.SimulateGame();

        // Assert
        (events.Last() is GameWon or GameDraw).ShouldBeTrue();
    }

    [Test]
    public void move_made_events_show_alternating_players() {
        // Act
        var (_, events, _) = TicTacToeSimulator.SimulateGame();
        var moveMadeEvents = events.OfType<MoveMade>().ToList();

        // Assert
        moveMadeEvents.ShouldNotBeEmpty();
        var gameStartedEvent = events.OfType<GameStarted>().Single();
        var expectedPlayer   = gameStartedEvent.StartingPlayer;

        foreach (var moveEvent in moveMadeEvents) {
            moveEvent.Player.ShouldBe(expectedPlayer);
            expectedPlayer = expectedPlayer == Player.X ? Player.O : Player.X;
        }
    }

    [Test]
    public void all_moves_are_made_on_empty_cells_during_simulation() {
        // Act
        var (_, events, _) = TicTacToeSimulator.SimulateGame();

        // Assert
        var boardCells = Board.CreateEmpty().Cells.ToDictionary(c => c.Position, c => c.State);

        foreach (var ev in events)
            if (ev is MoveMade moveMade) {
                boardCells[moveMade.Position].ShouldBe(CellState.Empty);
                boardCells[moveMade.Position] = (CellState)moveMade.Player;
            }
    }

    [Test]
    public void simulated_game_has_between_5_and_9_move_made_events_inclusive() {
        // Act
        var (_, events, _) = TicTacToeSimulator.SimulateGame();
        var moveCount = events.OfType<MoveMade>().Count();

        // Assert
        moveCount.ShouldBeInRange(5, 9);

        if (events.Last() is GameDraw)
            moveCount.ShouldBe(9);
    }

    [Test]
    public void simulated_game_events_can_reconstruct_a_consistent_final_game_state() {
        // Act
        var (gameId, events, winner) = TicTacToeSimulator.SimulateGame();

        // Arrange
        var reconstructedGame = new Game();

        foreach (var ev in events)
            (reconstructedGame, _) = ev switch {
                GameStarted gs => reconstructedGame.Execute(new StartGame(gs.GameId, gs.StartingPlayer)),
                MoveMade mm    => reconstructedGame.Execute(new MakeMove(mm.GameId, mm.Position, mm.Player)),
                _              => (reconstructedGame, [])
            };

        // Assert
        var lastEvent = events.Last();
        reconstructedGame.Id.ShouldBe(gameId);
        reconstructedGame.Winner.ShouldBe(winner);

        if (lastEvent is GameWon gameWon) {
            reconstructedGame.Status.ShouldBe(GameStatus.Won);
            reconstructedGame.Winner.ShouldBe(gameWon.Winner);
            // Verify winning line on reconstructed board
            Game.CheckWin(reconstructedGame.Board, gameWon.Winner, out var winningLineOnBoard).ShouldBeTrue();
            winningLineOnBoard.ShouldBeEquivalentTo(gameWon.WinningLine);
        }
        else if (lastEvent is GameDraw) {
            reconstructedGame.Status.ShouldBe(GameStatus.Draw);
            reconstructedGame.Winner.ShouldBeNull();
            reconstructedGame.Board.Cells.All(c => c.State != CellState.Empty).ShouldBeTrue();
        }
        else
            true.ShouldBeFalse("Last event was not GameWon or GameDraw"); // Should not happen
    }

    [Test]
    public void starting_player_is_either_x_or_o_across_multiple_simulations() {
        // Arrange
        const int simulationCount = 30; // Increased for better chance to see both

        var startingPlayers = new HashSet<Player>();

        // Act
        for (var i = 0; i < simulationCount; i++) {
            var (_, events, _) = TicTacToeSimulator.SimulateGame();
            var gameStartedEvent = events.First().ShouldBeOfType<GameStarted>();
            startingPlayers.Add(gameStartedEvent.StartingPlayer);
        }

        // Assert
        startingPlayers.Count.ShouldBeInRange(1, 2); // Could be 1 if extremely unlucky, but usually 2
        if (simulationCount > 10)                    // For a reasonable number of simulations, expect both
            startingPlayers.ShouldContain(Player.X);

        startingPlayers.ShouldContain(Player.O);
    }

    [Test]
    public void returned_game_id_matches_events_game_id() {
        // Act
        var (gameId, events, _) = TicTacToeSimulator.SimulateGame();

        // Assert
        var gameStartedEvent = events.OfType<GameStarted>().Single();
        gameId.ShouldBe(gameStartedEvent.GameId);

        // All events should have the same GameId
        foreach (var ev in events) {
            switch (ev) {
                case GameStarted gs:
                    gs.GameId.ShouldBe(gameId);
                    break;
                case MoveMade mm:
                    mm.GameId.ShouldBe(gameId);
                    break;
                case GameWon gw:
                    gw.GameId.ShouldBe(gameId);
                    break;
                case GameDraw gd:
                    gd.GameId.ShouldBe(gameId);
                    break;
            }
        }
    }

    [Test]
    public void returned_winner_matches_last_event_winner_or_null_for_draw() {
        // Act
        var (_, events, winner) = TicTacToeSimulator.SimulateGame();
        var lastEvent = events.Last();

        // Assert
        if (lastEvent is GameWon gameWon)
            winner.ShouldBe(gameWon.Winner);
        else if (lastEvent is GameDraw)
            winner.ShouldBeNull();
    }
}
