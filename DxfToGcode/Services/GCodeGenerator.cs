using System.Globalization;
using System.IO;
using System.Text;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

namespace DxfToGcode.Services;

public sealed record CopyConfiguration(string WorkOffset, bool MirrorLeftRight);

public sealed class GCodeGenerator
{
    private const double FeedRate = 30000;
    private const double LeadLength = 10.0;

    // Dokładność odwzorowania krzywych.
    private const double MaximumSegmentLength = 1.0;
    private const double MaximumCurveDeviation = 0.05;

    public string Generate(
        string dxfFilePath,
        short coldGlueColor,
        short hotGlueColor,
        short frameColor,
        IReadOnlyList<CopyConfiguration> copies)
    {
        var dxfFile = DxfFile.Load(dxfFilePath);

        var splines = dxfFile.Entities
            .OfType<DxfSpline>()
            .ToList();

        var frame = splines.SingleOrDefault(
            spline => spline.Color.RawValue == frameColor);

        if (frame is null)
        {
            throw new InvalidOperationException(
                "Nie znaleziono jednej ramki o wskazanym kolorze.");
        }

        var framePoints = SampleSpline(frame);

        var minX = framePoints.Min(point => point.X);
        var maxX = framePoints.Max(point => point.X);

        var output = new StringBuilder();

        output.AppendLine("; DxfToGcode");
        output.AppendLine($"; Plik źródłowy: {Path.GetFileName(dxfFilePath)}");
        output.AppendLine("G90");
        output.AppendLine("G49");
        output.AppendLine();

        foreach (var copy in copies)
        {
            output.AppendLine(copy.WorkOffset);

            string? activeTool = null;

            foreach (var spline in splines)
            {
                if (spline.Color.RawValue == frameColor)
                {
                    continue;
                }

                var tool = GetTool(
                    spline.Color.RawValue,
                    coldGlueColor,
                    hotGlueColor);

                if (tool is null)
                {
                    throw new InvalidOperationException(
                        $"Krzywa ma nieprzypisany kolor DXF: {spline.Color.RawValue}.");
                }

                if (activeTool != tool)
                {
                    output.AppendLine($"M6 {tool}");
                    activeTool = tool;
                }

                var points = SampleSpline(spline);

                if (copy.MirrorLeftRight)
                {
                    points = points
                        .Select(point => new Point2D(minX + maxX - point.X, point.Y))
                        .ToList();
                }

                AppendPath(output, points);
            }

            output.AppendLine();
        }

        output.AppendLine("G0 X0.0000 Y0.0000");
        output.AppendLine("M30");

        return output.ToString();
    }

    private static string? GetTool(
        short color,
        short coldGlueColor,
        short hotGlueColor)
    {
        if (color == coldGlueColor)
        {
            return "T1";
        }

        if (color == hotGlueColor)
        {
            return "T2";
        }

        return null;
    }

    private static void AppendPath(StringBuilder output, IReadOnlyList<Point2D> points)
    {
        if (points.Count < 2)
        {
            throw new InvalidOperationException(
                "Jedna z krzywych ma za mało punktów.");
        }

        var startDirection = Normalize(points[1] - points[0]);
        var endDirection = Normalize(points[^1] - points[^2]);

        var leadIn = points[0] - startDirection * LeadLength;
        var leadOut = points[^1] + endDirection * LeadLength;

        output.AppendLine($"G0 X{Format(leadIn.X)} Y{Format(leadIn.Y)}");
        output.AppendLine(
            $"G1 X{Format(points[0].X)} Y{Format(points[0].Y)} F{FeedRate}");
        output.AppendLine("M62 P1");

        foreach (var point in points.Skip(1))
        {
            output.AppendLine($"G1 X{Format(point.X)} Y{Format(point.Y)}");
        }

        output.AppendLine("M63 P1");
        output.AppendLine($"G1 X{Format(leadOut.X)} Y{Format(leadOut.Y)}");
    }

    private static List<Point2D> SampleSpline(DxfSpline spline)
    {
        if (spline.ControlPoints.Count <= spline.DegreeOfCurve)
        {
            throw new InvalidOperationException("Nieprawidłowa krzywa SPLINE.");
        }

        if (spline.KnotValues.Count == 0)
        {
            throw new InvalidOperationException("Krzywa SPLINE nie ma węzłów.");
        }

        var degree = spline.DegreeOfCurve;
        var start = spline.KnotValues[degree];
        var end = spline.KnotValues[spline.KnotValues.Count - degree - 1];

        var points = new List<Point2D>();
        var first = EvaluateSpline(spline, start);
        var last = EvaluateSpline(spline, end);

        points.Add(first);
        AddAdaptivePoints(spline, start, first, end, last, points, 0);

        return points;
    }

    private static void AddAdaptivePoints(
        DxfSpline spline,
        double startParameter,
        Point2D startPoint,
        double endParameter,
        Point2D endPoint,
        List<Point2D> points,
        int depth)
    {
        var middleParameter = (startParameter + endParameter) / 2.0;
        var middlePoint = EvaluateSpline(spline, middleParameter);

        var chordLength = Distance(startPoint, endPoint);
        var deviation = DistanceToLineSegment(
            middlePoint,
            startPoint,
            endPoint);

        if (depth >= 20 ||
            (chordLength <= MaximumSegmentLength &&
             deviation <= MaximumCurveDeviation))
        {
            points.Add(endPoint);
            return;
        }

        AddAdaptivePoints(
            spline,
            startParameter,
            startPoint,
            middleParameter,
            middlePoint,
            points,
            depth + 1);

        AddAdaptivePoints(
            spline,
            middleParameter,
            middlePoint,
            endParameter,
            endPoint,
            points,
            depth + 1);
    }

    private static Point2D EvaluateSpline(DxfSpline spline, double parameter)
    {
        var degree = spline.DegreeOfCurve;
        var knots = spline.KnotValues;
        var controlPoints = spline.ControlPoints;

        var start = knots[degree];
        var end = knots[knots.Count - degree - 1];

        if (parameter <= start)
        {
            return ToPoint(controlPoints[0]);
        }

        if (parameter >= end)
        {
            return ToPoint(controlPoints[^1]);
        }

        var x = 0.0;
        var y = 0.0;
        var totalWeight = 0.0;

        for (var index = 0; index < controlPoints.Count; index++)
        {
            var basis = BasisFunction(index, degree, parameter, knots);
            var weight = basis * controlPoints[index].Weight;

            x += controlPoints[index].Point.X * weight;
            y += controlPoints[index].Point.Y * weight;
            totalWeight += weight;
        }

        if (Math.Abs(totalWeight) < double.Epsilon)
        {
            throw new InvalidOperationException(
                "Nie można obliczyć punktu krzywej SPLINE.");
        }

        return new Point2D(x / totalWeight, y / totalWeight);
    }

    private static double BasisFunction(
        int index,
        int degree,
        double parameter,
        IList<double> knots)
    {
        if (degree == 0)
        {
            return knots[index] <= parameter && parameter < knots[index + 1]
                ? 1.0
                : 0.0;
        }

        var left = 0.0;
        var leftDenominator = knots[index + degree] - knots[index];

        if (leftDenominator != 0)
        {
            left = (parameter - knots[index]) / leftDenominator *
                   BasisFunction(index, degree - 1, parameter, knots);
        }

        var right = 0.0;
        var rightDenominator = knots[index + degree + 1] - knots[index + 1];

        if (rightDenominator != 0)
        {
            right = (knots[index + degree + 1] - parameter) / rightDenominator *
                    BasisFunction(index + 1, degree - 1, parameter, knots);
        }

        return left + right;
    }

    private static Point2D ToPoint(DxfControlPoint controlPoint) =>
        new(controlPoint.Point.X, controlPoint.Point.Y);

    private static Point2D Normalize(Point2D point)
    {
        var length = Math.Sqrt(point.X * point.X + point.Y * point.Y);

        if (length == 0)
        {
            throw new InvalidOperationException(
                "Nie można wyznaczyć kierunku najazdu dla krzywej.");
        }

        return new Point2D(point.X / length, point.Y / length);
    }

    private static double Distance(Point2D first, Point2D second) =>
        Math.Sqrt(
            Math.Pow(second.X - first.X, 2) +
            Math.Pow(second.Y - first.Y, 2));

    private static double DistanceToLineSegment(
        Point2D point,
        Point2D start,
        Point2D end)
    {
        var segment = end - start;
        var segmentLengthSquared =
            segment.X * segment.X + segment.Y * segment.Y;

        if (segmentLengthSquared == 0)
        {
            return Distance(point, start);
        }

        var projection =
            ((point.X - start.X) * segment.X +
             (point.Y - start.Y) * segment.Y) / segmentLengthSquared;

        projection = Math.Clamp(projection, 0.0, 1.0);

        var closestPoint = new Point2D(
            start.X + projection * segment.X,
            start.Y + projection * segment.Y);

        return Distance(point, closestPoint);
    }

    private static string Format(double value) =>
        value.ToString("0.0000", CultureInfo.InvariantCulture);

    private readonly record struct Point2D(double X, double Y)
    {
        public static Point2D operator +(Point2D left, Point2D right) =>
            new(left.X + right.X, left.Y + right.Y);

        public static Point2D operator -(Point2D left, Point2D right) =>
            new(left.X - right.X, left.Y - right.Y);

        public static Point2D operator *(Point2D point, double value) =>
            new(point.X * value, point.Y * value);
    }
}