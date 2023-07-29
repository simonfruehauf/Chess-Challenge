using ChessChallenge.API;
using System;
using System.Collections.Generic;
 using System.Linq;
public class MyBot : IChessBot
{
    Dictionary<ulong, int> transpositions = new(), quiesces = new();
    Queue<ulong> lastpositions = new(capacity:64);
    public Move Think(Board board, Timer timer)
    {
        Move move = PickMove(board, timer.MillisecondsRemaining >= 40000 ? 7 : timer.MillisecondsRemaining >= 20000 ? 5 : 6);
        board.MakeMove(move);
        lastpositions.Enqueue(board.ZobristKey);
        lastpositions.TrimExcess();

        return move;
    }
    /// <summary>
    /// Evaluates a current board based on mobility, material and other factors. Returns a score.
    /// </summary>
    int Eval(Board board)
    {
        int mobilityValue = 4 * board.GetLegalMoves().Length + 8 * board.GetLegalMoves(true).Length, pieceValue = 0, checkValue = 0; // I feel so bad about defining variables like this
        int[] materialValues = { 100, 320, 330, 500, 1000, board.PlyCount >= 25 ? 5000 : 10000 /*Give the King more value in lategame*/ };
        // Material values for P, N, B, R, Q, K
        // These values avoid exchanging minor pieces (N & B) for 3 minor pieces
        if (board.TrySkipTurn()) // Don't skip this if in check.
        {
            int skipLegalMovesCount = board.GetLegalMoves().Length, skipCaptureMovesCount = board.GetLegalMoves(true).Length;
            mobilityValue -= 4 * skipLegalMovesCount + 8 * skipCaptureMovesCount;
            if (board.IsInCheckmate()) mobilityValue += 2147483647;
            else if (board.IsInCheck()) mobilityValue += 120;
            else if (skipLegalMovesCount == 0) mobilityValue += 1000;
            board.UndoSkipTurn();
        }
        PieceList[] pieces = board.GetAllPieceLists();
        for (int i = 0; i < 6; i++)
        {
            // Calculate material value of current board        
            pieceValue += (pieces[i].Count - pieces[i + 6].Count) * materialValues[i];
            // calculate positional value of pieces
            bool x = false;
            do
            {
                double posValue = 0;
                foreach (Piece p in board.GetPieceList((PieceType)(i+1), x)) // x == 0 is black
                {
                    var (row,col) = CalcPosition(p.Square.Index,x);
                    switch ((PieceType)(i+1)){
                        case PieceType.Knight:
                            posValue = roundToNearest((MathF.Abs(row - 4.5f) + MathF.Abs(col - 4.5f)) / 2 * 23, 5)-30;
                            break;
                        case PieceType.Bishop:
                            posValue = roundToNearest((Math.Max(Math.Abs(row - 4.5), Math.Abs(col - 4.5)) * 10), 5)-20;
                            break;
                        case PieceType.Rook:
                            posValue = (col == 0 || col == 7) ? -5 : 0 + row == 2 ? 10 : 0;
                            posValue*= -1;
                            break;
                        case PieceType.Queen:
                            posValue = roundToNearest((MathF.Abs(row - 4.5f) + MathF.Abs(col - 4.5f)) / 4 * 10, 5)-10;
                            break;
                        case PieceType.Pawn:
                            posValue = 
                            row == 2 ? 50 : row == 7 && (col == 4 || col == 5) ? -20 : roundToNearest((MathF.Abs(row - 4.5f) + MathF.Abs(col - 4.5f)) / 2 * 23, 5)-30;
                            break;
                        default:
                            break;
                    }
                    pieceValue += (int)(posValue*1);

                }
                x = !x;
            } while (x);

        }

        if (board.IsInCheckmate()) checkValue -= 2147483647;
        else if (board.IsInCheck()) checkValue -= 120;
        else if (board.GetLegalMoves().Length == 0) checkValue += 1000;
        int lastPositionValue = lastpositions.Contains(board.ZobristKey) ? mobilityValue / 2 : 0;
        return (mobilityValue + pieceValue + checkValue + lastPositionValue) * (board.IsWhiteToMove ? 1 : -1);
    }
    /// <summary>
    /// Loops through legal moves and evaluates them with the AlphaBeta function.
    /// Always returns a valid move, but not neccesarily the best.
    /// </summary>
    Move PickMove(Board board, int depth)
    {
        int alpha = -2147483647;
        Move[] moves = board.GetLegalMoves();
        Move bestMove = moves.Length > 0 ? moves[0] : new();
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int value = -AlphaBeta(-int.MaxValue, -alpha, depth - DepthCheck(board), board);
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
    int AlphaBeta(int alpha, int beta, int depth, Board board)
    {
        if (depth <= 0) return Quiesce(alpha, beta, 4, board);
        foreach (Move move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            if (!transpositions.TryGetValue(board.ZobristKey, out int score))
            {
                score = -AlphaBeta(-beta, -alpha, depth - DepthCheck(board), board);
                transpositions[board.ZobristKey] = score;
            }
            board.UndoMove(move);
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        return alpha;
    }
    int Quiesce(int alpha, int beta, int depth, Board board)
    {
        int stand_pat = Eval(board);
        if (depth <= 0) return stand_pat;
        if (stand_pat >= beta || alpha < stand_pat) alpha = stand_pat;
        foreach (Move move in board.GetLegalMoves())
        {
            if (move.IsCapture || move.IsPromotion)
            {
                board.MakeMove(move);
                if (!quiesces.TryGetValue(board.ZobristKey, out int score))
                {
                    score = -Quiesce(-beta, -alpha, depth - DepthCheck(board), board);
                    quiesces[board.ZobristKey] = score;
                }
                board.UndoMove(move);
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
        }
        return alpha;
    }
    int DepthCheck(Board board) => board.IsInCheck() ? 1 : 2;

    Tuple<int, int> CalcPosition(int index, bool white)
    {
        return Tuple.Create(Math.Abs(index % 8 - (white ? -63 : 0)) /*row*/, Math.Abs((int)Math.Floor(index / 8f) - (white ? -63 : 0)) /*col*/);
    }
    double roundToNearest(double value, double to){
        return Math.Round(value/ to) * to;
    }
    
}