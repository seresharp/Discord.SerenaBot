using Microsoft.CodeAnalysis.Text;
using Remora.Commands.Attributes;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Results;
using SerenaBot.Commands.Util;
using SerenaBot.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerenaBot.Commands;

public class MinesweeperCommands : BaseCommandGroup
{
    public static readonly string MineEmoji = "\uD83D\uDCA5";
    public static readonly string NumberEmoji = "️\uFE0F";

    [Command("minesweeper")]
    [CommandType(ApplicationCommandType.ChatInput)]
    [Description("Returns a completable minesweeper board with the given width, height, and amount of mines")]
    public async Task<IResult> CreateMinesweeperBoardAsync(int width, int height, int mines)
    {
        if (width <= 0 || height <= 0 || mines <= 0)
        {
            return await Feedback.SendContextualInfoAsync("Width, height, and mines must all be at least 1");
        }

        if (width * 5 < height || height * 5 < width)
        {
            return await Feedback.SendContextualInfoAsync("Board dimensions must have a ratio of at most 1:5");
        }

        if (width * height > 333)
        {
            return await Feedback.SendContextualInfoAsync("Board size (w * h) must not be greater than 333");
        }

        if (mines > (width * height) / 4)
        {
            return await Feedback.SendContextualInfoAsync("Board can be no more than 25% mines");
        }

        (bool[,] minePositions, (int x, int y) startPos) = GenerateMines(width, height, mines);
        TrySolveMinesweeper(minePositions, startPos);

        return await Feedback.SendContextualInfoAsync("This doesn't work yet it's just here for debugging stuff behind the scenes");
    }

    private static int AdjacentMineCount(bool[,] minePositions, int x, int y)
    {
        int mines = 0;
        for (int i = x - 1; i <= x + 1; i++)
        {
            for (int j = y - 1; j <= y + 1; j++)
            {
                if (i < 0 || i >= minePositions.GetLength(0) || j < 0 || j >= minePositions.GetLength(1) || (i == x && j == y))
                {
                    continue;
                }

                if (minePositions[i, j]) mines++;
            }
        }

        return mines;
    }

    private static (bool[,] minePositions, (int x, int y) startSquare) GenerateMines(int width, int height, int mines)
    {
        bool[,] minePositions = new bool[width, height];
        int[,] board = new int[width, height];

        (int x, int y)[] allPositions = Enumerable.Range(0, width)
            .SelectMany(x => Enumerable.Range(0, height).Select(y => (x, y)))
            .ToArray();

        for (int i = 0; i < mines; i++)
        {
            (int x, int y) = allPositions
                .Where(p => !minePositions[p.x, p.y])
                .ToArray()
                .GetRandomItem();

            minePositions[x, y] = true;
        }

        (int x, int y)[] validStarts = allPositions
            .Where(p => !minePositions[p.x, p.y] && AdjacentMineCount(minePositions, p.x, p.y) == 0)
            .ToArray();

        if (validStarts.Length == 0) return GenerateMines(width, height, mines);

        return (minePositions, validStarts.GetRandomItem());
    }

    private static bool TrySolveMinesweeper(bool[,] mines, (int x, int y) startPos)
    {
        const int UNKNOWN = -1;
        const int MINE = -2;

        int[,] board = new int[mines.GetLength(0), mines.GetLength(1)];
        Enumerable.Range(0, board.GetLength(0))
            .SelectMany(x => Enumerable.Range(0, board.GetLength(1)).Select(y => (x, y)))
            .ToList()
            .ForEach(pos => board[pos.x, pos.y] = UNKNOWN);

        board[startPos.x, startPos.y] = 0;
        (int x, int y)[] allPositions = Enumerable.Range(0, board.GetLength(0))
            .SelectMany(x => Enumerable.Range(0, board.GetLength(1)).Select(y => (x, y)))
            .ToArray();

        while (true)
        {
            bool changedBoard = false;

            // Click spots adjacent to zeroes
            foreach ((int x, int y) in allPositions.Where(pos => board[pos.x, pos.y] == 0))
            {
                changedBoard |= TryPropagateZeroes(x, y);
            }

            // Mark adjacent squares as mines in cases where adjacent unknown + adjacent known mines = adjacent mine count
            foreach ((int x, int y) in allPositions.Where(pos => board[pos.x, pos.y] >= 0))
            {
                (int x, int y)[] unknown = GetAdjacentUnknown(x, y);
                if (unknown.Length == 0 || GetAdjacentMines(x, y).Length + unknown.Length != board[x, y])
                {
                    continue;
                }

                foreach ((int adjX, int adjY) in unknown)
                {
                    board[adjX, adjY] = MINE;
                    changedBoard = true;
                }
            }

            // Click adjacent squares in cases where adjacent known mines = adjacent mine count
            foreach ((int x, int y) in allPositions.Where(pos => board[pos.x, pos.y] == GetAdjacentMines(pos.x, pos.y).Length))
            {
                foreach ((int adjX, int adjY) in GetAdjacentUnknown(x, y))
                {
                    board[adjX, adjY] = AdjacentMineCount(mines, adjX, adjY);
                    changedBoard = true;
                }
            }

            if (allPositions.All(p => mines[p.x, p.y] == (board[p.x, p.y] == MINE)))
            {
                return true;
            }

            if (!changedBoard)
            {
                StringBuilder among = new();
                for (int x = 0; x < board.GetLength(0); x++)
                {
                    for (int y = 0; y < board.GetLength(1); y++)
                    {
                        among.Append(board[x, y] switch
                        {
                            UNKNOWN => 'U',
                            MINE => 'M',
                            int i => (char)(48 + i)
                        });
                    }

                    among.AppendLine();
                }

                Console.WriteLine(among.ToString());
                return false;
            }
        }

        bool TryPropagateZeroes(int x, int y)
        {
            bool changedBoard = false;
            foreach ((int adjX, int adjY) in GetAdjacentUnknown(x, y))
            {
                if (board[adjX, adjY] == UNKNOWN)
                {
                    board[adjX, adjY] = AdjacentMineCount(mines, adjX, adjY);
                    changedBoard = true;
                }

                if (board[adjX, adjY] == 0) changedBoard |= TryPropagateZeroes(adjX, adjY);
            }

            return changedBoard;
        }

        (int x, int y)[] GetAdjacentUnknown(int x, int y)
            => GetAdjacent(x, y, UNKNOWN);

        (int x, int y)[] GetAdjacentMines(int x, int y)
            => GetAdjacent(x, y, MINE);

        (int x, int y)[] GetAdjacent(int x, int y, int value)
        {
            List<(int x, int y)> adj = new();
            for (int i = x - 1; i <= x + 1; i++)
            {
                for (int j = y - 1; j <= y + 1; j++)
                {
                    if (i < 0 || i >= board.GetLength(0) || j < 0 || j >= board.GetLength(1) || (i == x && j == y))
                    {
                        continue;
                    }

                    if (board[i, j] == value) adj.Add((i, j));
                }
            }

            return adj.ToArray();
        }
    }
}
