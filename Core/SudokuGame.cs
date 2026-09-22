namespace HammerOS.Core;

public sealed class SudokuGame
{
    public int[] Givens { get; set; } = new int[81];
    public int[] Cells { get; set; } = new int[81];
    public int[] Notes { get; set; } = new int[81];
    public int[] Solution { get; set; } = new int[81];
    public string Id { get; set; } = "";
    public bool Solved => Cells.SequenceEqual(Solution);
    public bool Valid => Givens is { Length: 81 } && Cells is { Length: 81 } && Notes is { Length: 81 } && Solution is { Length: 81 } && Solution.All(x => x is >= 1 and <= 9) && Cells.All(x => x is >= 0 and <= 9) && Givens.Select((x, i) => x == 0 || x == Solution[i] && Cells[i] == x).All(x => x);
    public static SudokuGame Create()
    {
        var random = Random.Shared;
        int[] Order() => Enumerable.Range(0, 3).OrderBy(_ => random.Next()).SelectMany(b => Enumerable.Range(0, 3).OrderBy(_ => random.Next()).Select(r => b * 3 + r)).ToArray();
        var rows = Order(); var columns = Order(); var digits = Enumerable.Range(1, 9).OrderBy(_ => random.Next()).ToArray();
        var game = new SudokuGame { Id = DateTime.Now.ToString("HHmm") + "-" + random.Next(100, 999) };
        for (var i = 0; i < 81; i++) game.Solution[i] = digits[(rows[i / 9] * 3 + rows[i / 9] / 3 + columns[i % 9]) % 9];
        game.Givens = (int[])game.Solution.Clone(); var removed = 0;
        foreach (var cell in Enumerable.Range(0, 81).OrderBy(_ => random.Next()))
        {
            var value = game.Givens[cell]; game.Givens[cell] = 0;
            if (CountSolutions(game.Givens) != 1) game.Givens[cell] = value; else removed++;
            if (removed >= 45) break;
        }
        game.Cells = (int[])game.Givens.Clone(); return game;
    }
    public static int CountSolutions(int[] puzzle)
    {
        var board = (int[])puzzle.Clone();
        int Search()
        {
            var index = -1; int[]? candidates = null;
            for (var i = 0; i < 81; i++) if (board[i] == 0)
            {
                var possible = Enumerable.Range(1, 9).Where(n => !Peers(i).Any(p => board[p] == n)).ToArray();
                if (possible.Length == 0) return 0;
                if (candidates is null || possible.Length < candidates.Length) { index = i; candidates = possible; if (possible.Length == 1) break; }
            }
            if (index < 0) return 1;
            var count = 0; foreach (var n in candidates!) { board[index] = n; count += Search(); if (count >= 2) break; } board[index] = 0; return count;
        }
        return Search();
    }
    public static IEnumerable<int> Peers(int index) => Enumerable.Range(0, 81).Where(i => i != index && (i / 9 == index / 9 || i % 9 == index % 9 || i / 27 == index / 27 && i % 9 / 3 == index % 9 / 3));
    public bool Conflict(int index) => Cells[index] != 0 && Peers(index).Any(i => Cells[i] == Cells[index]);
    public void Enter(int index, int value, bool pencil = false)
    {
        if (index is < 0 or > 80 || value is < 0 or > 9 || Givens[index] != 0) return;
        if (pencil && value > 0) { Cells[index] = 0; Notes[index] ^= 1 << value; }
        else { Cells[index] = value; Notes[index] = 0; if (value > 0) foreach (var peer in Peers(index)) Notes[peer] &= ~(1 << value); }
    }
    public void Reset() { Cells = (int[])Givens.Clone(); Notes = new int[81]; }
}
