using System.Text;
using System.Text.Json.Nodes;
using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.TestSupport;

/// <summary>
/// Makes the malformed files a test needs, from a file built through
/// <see cref="XgFileBuilder"/>: a game whose standing the session refuses and
/// a play holding a corrupt candidate. The shipped library has no door to
/// either, rightly, so each edit changes a copy of the file's JSON document and
/// reads it back through the public read. The result is an ordinary parsed
/// <see cref="XgFile"/>, which the writers write as usual, and the input is
/// left as it was.
/// </summary>
/// <remarks>
/// Games and moves are numbered from 1, as a decision's id and an
/// <see cref="XgOmission"/> number them. For test projects only: the assembly
/// is unsupported on the browser, so a browser project cannot use it.
/// </remarks>
public static class XgFileEdits
{
    /// <summary>
    /// <paramref name="file"/> with the headers of <paramref name="games"/>
    /// saying player 1 has already won the match, a standing the session
    /// refuses, so the walk omits each of those games whole
    /// (<see cref="XgOmissionKind.InvalidStanding"/>) and keeps the numbers of
    /// the rest. The file must be a match: a money game has no such standing,
    /// so there the edit refuses nothing.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> or <paramref name="games"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">One of <paramref name="games"/> is not a game of the file.</exception>
    public static XgFile WithRefusedGames(XgFile file, params int[] games)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(games);

        return Edited(file, records =>
        {
            var headers = records.Where(record => TypeOf(record) == "HeaderGame").ToList();
            int length = (int)records.Single(record => TypeOf(record) == "HeaderMatch")!["matchLength"]!;
            foreach (int game in games)
                GameHeader(headers, game, nameof(games))["score1"] = length;
        });
    }

    /// <summary>
    /// <paramref name="file"/> with the first candidate of a move opening from
    /// the 2-point, so the walk omits that move
    /// (<see cref="XgOmissionKind.CorruptCandidate"/>) and keeps the decisions
    /// around it. The move must be analysed, and an unanalysed one is refused.
    /// Its mover must have no checker on the 2-point, as at the opening: that
    /// is the caller's to ensure, since with one there the candidate can be a
    /// legal play and the walk omits nothing.
    /// </summary>
    /// <param name="file">The file to copy and edit.</param>
    /// <param name="game">The game's number within the file.</param>
    /// <param name="move">
    /// The move's number within the game, counting move records and no cube
    /// records: the number the omission reports.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="game"/> is not a game of the file, or <paramref name="move"/> is not a move of that game.</exception>
    /// <exception cref="ArgumentException"><paramref name="move"/> is not analysed.</exception>
    public static XgFile WithCorruptCandidate(XgFile file, int game, int move)
    {
        ArgumentNullException.ThrowIfNull(file);

        return Edited(file, records =>
        {
            var headers = records.Where(record => TypeOf(record) == "HeaderGame").ToList();
            var header = GameHeader(headers, game, nameof(game));
            var moves = records
                .SkipWhile(record => record != header)
                .Skip(1)
                .TakeWhile(record => TypeOf(record) != "HeaderGame")
                .Where(record => TypeOf(record) == "Move")
                .ToList();
            if (move < 1 || move > moves.Count)
                throw new ArgumentOutOfRangeException(nameof(move), move, $"Game {game} holds {moves.Count} moves, numbered from 1.");

            var candidates = moves[move - 1]!["analysis"]!["moves"]!.AsArray();
            if (candidates.Count == 0)
                throw new ArgumentException($"Move {move} of game {game} is not analysed, so it holds no candidate.", nameof(move));

            var candidate = candidates[0]!;
            candidate[0] = 1;
            candidate[1] = 0;
        });
    }

    /// <summary>The header of the 1-based <paramref name="game"/>, or the refusal naming <paramref name="parameter"/>.</summary>
    private static JsonNode GameHeader(List<JsonNode?> headers, int game, string parameter)
    {
        if (game < 1 || game > headers.Count)
            throw new ArgumentOutOfRangeException(parameter, game, $"The file holds {headers.Count} games, numbered from 1.");
        return headers[game - 1]!;
    }

    /// <summary><paramref name="file"/> with <paramref name="edit"/> applied to its JSON document's records, read back.</summary>
    private static XgFile Edited(XgFile file, Action<JsonArray> edit)
    {
        var document = JsonNode.Parse(XgFileReader.ToJson(file))!;
        edit(document["records"]!.AsArray());

        var edited = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
        return XgSourceReader.ReadStream("edited.json", edited, XgSourceFormat.Json)
            .Match(decoded => decoded.File, failed => throw failed.Exception);
    }

    private static string? TypeOf(JsonNode? record) => (string?)record!["$type"];
}
