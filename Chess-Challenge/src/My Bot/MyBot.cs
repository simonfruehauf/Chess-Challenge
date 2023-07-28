using ChessChallenge.API;
using System.Collections.Generic;
public class MyBot : IChessBot
{
    Dictionary<ulong, int> transpositions = new(), quiesces = new();
    Queue<ulong> lastpositions = new();
    public Move Think(Board board, Timer timer)
    {
        Move move = pick_move(board, timer.MillisecondsRemaining >= 40000 ? 7 : timer.MillisecondsRemaining >= 20000 ? 5 : 6);
        board.MakeMove(move);
        lastpositions.Enqueue(board.ZobristKey);
        if (lastpositions.Count > 50) lastpositions.Dequeue();
        return move;
    }
    /// <summary>
    /// Evaluates a current board based on mobility, material and other factors. Returns a score.
    /// </summary>
    int eval(Board board)
    {
        int mobilityValue = 4 * board.GetLegalMoves().Length + 8 * board.GetLegalMoves(true).Length, pieceValue = 0, checkValue = 0; // I feel so bad about defining variables like this
        int[] materialvalues = { 100, 320, 330, 500, 900, board.PlyCount >= 25 ? 10000 : 20000 };  // Give the King more value in lategame
        // Material values for P, N, B, R, Q, K
        // These values avoid exchanging minor pieces (N & B) for 3 minor pieces
        if (board.TrySkipTurn()) // Don't skip this if in check.
        {
            int skipLegalMovesCount = board.GetLegalMoves().Length, skipCaptureMovesCount = board.GetLegalMoves(true).Length;
            mobilityValue -= 4 * skipLegalMovesCount + 8 * skipCaptureMovesCount;
            if (board.IsInCheckmate()) mobilityValue += 2147483647;
            else if (board.IsInCheck()) mobilityValue += 170;
            else if (skipLegalMovesCount == 0) mobilityValue += 1500;
            board.UndoSkipTurn();
        }
        PieceList[] pieces = board.GetAllPieceLists();
        for (int i = 0; i < 6; i++) pieceValue += (pieces[i].Count - pieces[i + 6].Count) * materialvalues[i]; // Calculate value of current board        
        if (board.IsInCheckmate()) checkValue -= 2147483647;
        else if (board.IsInCheck()) checkValue -= 170;
        else if (board.GetLegalMoves().Length == 0) checkValue += 2000;
        int lastPositionValue = lastpositions.Contains(board.ZobristKey) ? mobilityValue / 2 : 0;
        return (mobilityValue + pieceValue + checkValue + lastPositionValue) * (board.IsWhiteToMove ? 1 : -1);
    }
    /// <summary>
    /// Loops through legal moves and evaluates them with the AlphaBeta function.
    /// Always returns a valid move, but not neccesarily the best.
    /// </summary>
    Move pick_move(Board board, int depth)
    {
        int alpha = -2147483647;
        Move[] moves = board.GetLegalMoves();
        Move bestMove = moves.Length > 0 ? moves[0] : new();
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int value = -alpha_beta(-int.MaxValue, -alpha, depth - depth_check(board), board);
            board.UndoMove(move);
            if (value > alpha)
            {
                alpha = value;
                bestMove = move;
            }
        }
        return bestMove;
    }
    /// <summary>
    /// Recursively calls itself until it hits its maximum depth, then evaluates with the quiesce function.
    /// Also saves already evaluated values in the transpositions variable for later access.
    /// </summary>
    int alpha_beta(int alpha, int beta, int depth, Board board)
    {
        if (depth <= 0) return quiesce(alpha, beta, 4, board);
        foreach (Move move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            if (!transpositions.TryGetValue(board.ZobristKey, out int score))
            {
                score = -alpha_beta(-beta, -alpha, depth - depth_check(board), board);
                transpositions[board.ZobristKey] = score;
            }
            board.UndoMove(move);
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        return alpha;
    }
    int quiesce(int alpha, int beta, int depth, Board board)
    {
        int stand_pat = eval(board);
        if (depth <= 0) return stand_pat;
        if (stand_pat >= beta || alpha < stand_pat) alpha = stand_pat;
        foreach (Move move in board.GetLegalMoves())
        {
            if (move.IsCapture || move.IsPromotion)
            {
                board.MakeMove(move);
                if (!quiesces.TryGetValue(board.ZobristKey, out int score))
                {
                    score = -quiesce(-beta, -alpha, depth - depth_check(board), board);
                    quiesces[board.ZobristKey] = score;
                }
                board.UndoMove(move);
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
        }
        return alpha;
    }
    int depth_check(Board board) => board.IsInCheck() ? 1 : 2;
}