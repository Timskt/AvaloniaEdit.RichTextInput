using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace AvaloniaEdit.Editing
{
    /// <summary>
    /// Controls the underline used for one clause of an IME preedit string.
    /// </summary>
    public enum ImePreeditUnderlineStyle
    {
        /// <summary>A single solid underline.</summary>
        Solid,
        /// <summary>No underline.</summary>
        None,
        /// <summary>A thicker solid underline, normally used for the active clause.</summary>
        Thick,
        /// <summary>Two solid underlines.</summary>
        Double,
        /// <summary>A dotted underline.</summary>
        Dotted,
        /// <summary>A dashed underline.</summary>
        Dashed
    }

    /// <summary>
    /// Describes the visual decoration for a range in an IME preedit string.
    /// </summary>
    /// <remarks>
    /// Avalonia's current <see cref="Avalonia.Input.TextInput.TextInputMethodClient"/> API exposes the
    /// preedit text and cursor offset, but not clause attributes. Applications that receive clause
    /// attributes from an IME can use <see cref="TextArea.SetImePreeditText"/> with these values.
    /// Native preedit notifications that contain no clause attributes continue to use one solid
    /// underline for the complete string.
    /// </remarks>
    public sealed class ImePreeditClause
    {
        public ImePreeditClause(int start, int length)
            : this(start, length, ImePreeditUnderlineStyle.Solid, null)
        {
        }

        public ImePreeditClause(int start, int length, ImePreeditUnderlineStyle underlineStyle)
            : this(start, length, underlineStyle, null)
        {
        }

        public ImePreeditClause(int start, int length, ImePreeditUnderlineStyle underlineStyle, IBrush underlineBrush)
        {
            if (start < 0)
                throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            Start = start;
            Length = length;
            UnderlineStyle = underlineStyle;
            UnderlineBrush = underlineBrush;
        }

        /// <summary>Gets the UTF-16 start offset within the preedit string.</summary>
        public int Start { get; }

        /// <summary>Gets the UTF-16 length of the clause.</summary>
        public int Length { get; }

        /// <summary>Gets the underline style for the clause.</summary>
        public ImePreeditUnderlineStyle UnderlineStyle { get; }

        /// <summary>Gets an optional underline brush. The preedit foreground is used when this is null.</summary>
        public IBrush UnderlineBrush { get; }

        /// <summary>Gets the exclusive end offset of the clause.</summary>
        public int End => checked(Start + Length);
    }

    internal static class ImePreeditClauseCollection
    {
        public static IReadOnlyList<ImePreeditClause> Normalize(string text, IReadOnlyList<ImePreeditClause> clauses)
        {
            if (clauses == null || clauses.Count == 0 || string.IsNullOrEmpty(text))
                return null;

            var ordered = clauses.Where(clause => clause != null && clause.Length > 0)
                .OrderBy(clause => clause.Start)
                .ToArray();
            if (ordered.Length == 0)
                return null;

            var result = new List<ImePreeditClause>(ordered.Length * 2 + 1);
            var position = 0;
            foreach (var clause in ordered)
            {
                var end = (long)clause.Start + clause.Length;
                if (clause.Start < position)
                    throw new ArgumentException("IME preedit clauses must not overlap.", nameof(clauses));
                if (clause.Start > text.Length || end > text.Length)
                    throw new ArgumentOutOfRangeException(nameof(clauses), "IME preedit clause ranges must be within the preedit string.");

                if (clause.Start > position)
                    result.Add(new ImePreeditClause(position, clause.Start - position));

                result.Add(clause);
                position = (int)end;
            }

            if (position < text.Length)
                result.Add(new ImePreeditClause(position, text.Length - position));

            return result.ToArray();
        }
    }
}
