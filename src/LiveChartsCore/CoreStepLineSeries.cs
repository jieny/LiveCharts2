﻿
// The MIT License(MIT)
//
// Copyright(c) 2021 Alberto Rodriguez Orozco & LiveCharts Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Segments;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.VisualElements;

namespace LiveChartsCore;

/// <summary>
/// Defines the data to plot as a line.
/// </summary>
/// <typeparam name="TModel">The type of the model to plot.</typeparam>
/// <typeparam name="TVisual">The type of the visual point.</typeparam>
/// <typeparam name="TLabel">The type of the data label.</typeparam>
/// <typeparam name="TDrawingContext">The type of the drawing context.</typeparam>
/// <typeparam name="TPathGeometry">The type of the path geometry.</typeparam>
/// <typeparam name="TLineGeometry">The type of the line geometry</typeparam>
public class CoreStepLineSeries<TModel, TVisual, TLabel, TDrawingContext, TPathGeometry, TLineGeometry>
    : StrokeAndFillCartesianSeries<TModel, TVisual, TLabel, TDrawingContext>, IStepLineSeries<TDrawingContext>
        where TPathGeometry : IVectorGeometry<StepLineSegment, TDrawingContext>, new()
        where TVisual : class, ISizedGeometry<TDrawingContext>, new()
        where TLabel : class, ILabelGeometry<TDrawingContext>, new()
        where TLineGeometry: ILineGeometry<TDrawingContext>, new()
        where TDrawingContext : DrawingContext
{
    private readonly Dictionary<object, List<TPathGeometry>> _fillPathHelperDictionary = [];
    private readonly Dictionary<object, List<TPathGeometry>> _strokePathHelperDictionary = [];
    private float _geometrySize = 14f;
    private IPaint<TDrawingContext>? _geometryFill;
    private IPaint<TDrawingContext>? _geometryStroke;
    private bool _enableNullSplitting = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoreStepLineSeries{TModel, TVisual, TLabel, TDrawingContext, TPathGeometry, TLineGeometry}"/> class.
    /// </summary>
    /// <param name="isStacked">if set to <c>true</c> [is stacked].</param>
    /// <param name="values">The values.</param>
    public CoreStepLineSeries(ICollection<TModel>? values, bool isStacked = false)
        : base(GetProperties(isStacked), values)
    {
        DataPadding = new LvcPoint(0.5f, 1f);
    }

    /// <inheritdoc cref="IStepLineSeries{TDrawingContext}.EnableNullSplitting"/>
    public bool EnableNullSplitting { get => _enableNullSplitting; set => SetProperty(ref _enableNullSplitting, value); }

    /// <inheritdoc cref="IStepLineSeries{TDrawingContext}.GeometrySize"/>
    public double GeometrySize { get => _geometrySize; set => SetProperty(ref _geometrySize, (float)value); }

    /// <inheritdoc cref="IStepLineSeries{TDrawingContext}.GeometryFill"/>
    public IPaint<TDrawingContext>? GeometryFill
    {
        get => _geometryFill;
        set => SetPaintProperty(ref _geometryFill, value);
    }

    /// <inheritdoc cref="IStepLineSeries{TDrawingContext}.GeometrySize"/>
    public IPaint<TDrawingContext>? GeometryStroke
    {
        get => _geometryStroke;
        set => SetPaintProperty(ref _geometryStroke, value, true);
    }

    /// <inheritdoc cref="ChartElement{TDrawingContext}.Invalidate(Chart{TDrawingContext})"/>
    public override void Invalidate(Chart<TDrawingContext> chart)
    {
        var cartesianChart = (CartesianChart<TDrawingContext>)chart;
        var primaryAxis = cartesianChart.YAxes[ScalesYAt];
        var secondaryAxis = cartesianChart.XAxes[ScalesXAt];

        var drawLocation = cartesianChart.DrawMarginLocation;
        var drawMarginSize = cartesianChart.DrawMarginSize;
        var secondaryScale = secondaryAxis.GetNextScaler(cartesianChart);
        var primaryScale = primaryAxis.GetNextScaler(cartesianChart);
        var actualSecondaryScale = secondaryAxis.GetActualScaler(cartesianChart);
        var actualPrimaryScale = primaryAxis.GetActualScaler(cartesianChart);

        var gs = _geometrySize;
        var hgs = gs / 2f;
        var sw = Stroke?.StrokeThickness ?? 0;
        var p = primaryScale.ToPixels(pivot);

        // see note #240222
        var segments = _enableNullSplitting
            ? Fetch(cartesianChart).SplitByNullGaps(point => DeleteNullPoint(point, secondaryScale, primaryScale))
            : [Fetch(cartesianChart)];

        var stacker = (SeriesProperties & SeriesProperties.Stacked) == SeriesProperties.Stacked
            ? cartesianChart.SeriesContext.GetStackPosition(this, GetStackGroup())
            : null;

        var actualZIndex = ZIndex == 0 ? ((ISeries)this).SeriesId : ZIndex;
        var clipping = GetClipRectangle(cartesianChart);

        if (stacker is not null)
        {
            // see note #010621
            actualZIndex = 1000 - stacker.Position;
            if (Fill is not null) Fill.ZIndex = actualZIndex;
            if (Stroke is not null) Stroke.ZIndex = actualZIndex;
        }

        var dls = (float)DataLabelsSize;

        var segmentI = 0;
        var pointsCleanup = ChartPointCleanupContext.For(everFetched);

        if (!_strokePathHelperDictionary.TryGetValue(chart.Canvas.Sync, out var strokePathHelperContainer))
        {
            strokePathHelperContainer = [];
            _strokePathHelperDictionary[chart.Canvas.Sync] = strokePathHelperContainer;
        }

        if (!_fillPathHelperDictionary.TryGetValue(chart.Canvas.Sync, out var fillPathHelperContainer))
        {
            fillPathHelperContainer = [];
            _fillPathHelperDictionary[chart.Canvas.Sync] = fillPathHelperContainer;
        }

        var uwx = secondaryScale.MeasureInPixels(secondaryAxis.UnitWidth);
        uwx = uwx < gs ? gs : uwx;
        var hasSvg = this.HasVariableSvgGeometry();

        var isFirstDraw = !chart._drawnSeries.Contains(((ISeries)this).SeriesId);

        foreach (var segment in segments)
        {
            var hasPaths = false;
            var isSegmentEmpty = true;
            VectorManager<StepLineSegment, TDrawingContext>? strokeVector = null, fillVector = null;

            double previousPrimary = 0, previousSecondary = 0;

            foreach (var point in segment)
            {
                if (!hasPaths)
                {
                    hasPaths = true;

                    var fillLookup = GetSegmentVisual(segmentI, fillPathHelperContainer, VectorClosingMethod.CloseToPivot);
                    var strokeLookup = GetSegmentVisual(segmentI, strokePathHelperContainer, VectorClosingMethod.NotClosed);

                    if (fillLookup.Path.Commands.Count == 1)
                    {
                        Fill?.RemoveGeometryFromPainTask(cartesianChart.Canvas, fillLookup.Path);
                        fillLookup.Path.Commands.Clear();
                        fillPathHelperContainer.RemoveAt(segmentI);

                        fillLookup = GetSegmentVisual(segmentI, fillPathHelperContainer, VectorClosingMethod.CloseToPivot);
                    }

                    if (strokeLookup.Path.Commands.Count == 1)
                    {
                        Stroke?.RemoveGeometryFromPainTask(cartesianChart.Canvas, strokeLookup.Path);
                        strokeLookup.Path.Commands.Clear();
                        strokePathHelperContainer.RemoveAt(segmentI);

                        strokeLookup = GetSegmentVisual(segmentI, strokePathHelperContainer, VectorClosingMethod.NotClosed);
                    }

                    var isNew = fillLookup.IsNew || strokeLookup.IsNew;
                    var fillPath = fillLookup.Path;
                    var strokePath = strokeLookup.Path;

                    strokeVector = new VectorManager<StepLineSegment, TDrawingContext>(strokePath);
                    fillVector = new VectorManager<StepLineSegment, TDrawingContext>(fillPath);

                    if (Fill is not null)
                    {
                        Fill.AddGeometryToPaintTask(cartesianChart.Canvas, fillPath);
                        cartesianChart.Canvas.AddDrawableTask(Fill);
                        Fill.ZIndex = actualZIndex + 0.1;
                        Fill.SetClipRectangle(cartesianChart.Canvas, clipping);
                        fillPath.Pivot = p;
                        if (isNew)
                        {
                            fillPath.Animate(EasingFunction ?? cartesianChart.EasingFunction, AnimationsSpeed ?? cartesianChart.AnimationsSpeed);
                        }
                    }
                    if (Stroke is not null)
                    {
                        Stroke.AddGeometryToPaintTask(cartesianChart.Canvas, strokePath);
                        cartesianChart.Canvas.AddDrawableTask(Stroke);
                        Stroke.ZIndex = actualZIndex + 0.2;
                        Stroke.SetClipRectangle(cartesianChart.Canvas, clipping);
                        strokePath.Pivot = p;
                        if (isNew)
                        {
                            strokePath.Animate(EasingFunction ?? cartesianChart.EasingFunction, AnimationsSpeed ?? cartesianChart.AnimationsSpeed);
                        }
                    }

                    strokePath.Opacity = IsVisible ? 1 : 0;
                    fillPath.Opacity = IsVisible ? 1 : 0;
                }

                var coordinate = point.Coordinate;

                isSegmentEmpty = false;
                var s = 0d;
                if (stacker is not null)
                    s = coordinate.PrimaryValue > 0
                        ? stacker.GetStack(point).Start
                        : stacker.GetStack(point).NegativeStart;

                var visual = (StepLineVisualPoint<TDrawingContext, TVisual>?)point.Context.AdditionalVisuals;
                var dp = coordinate.PrimaryValue + s - previousPrimary;
                var ds = coordinate.SecondaryValue - previousSecondary;

                if (!IsVisible)
                {
                    if (visual is not null)
                    {
                        visual.Geometry.X = secondaryScale.ToPixels(coordinate.SecondaryValue);
                        visual.Geometry.Y = p;
                        visual.Geometry.Opacity = 0;
                        visual.Geometry.RemoveOnCompleted = true;

                        visual.StepSegment.Xi = secondaryScale.ToPixels(coordinate.SecondaryValue - ds);
                        visual.StepSegment.Xj = secondaryScale.ToPixels(coordinate.SecondaryValue);
                        visual.StepSegment.Yi = p;
                        visual.StepSegment.Yj = p;

                        point.Context.Visual = null;
                    }

                    pointsCleanup.Clean(point);

                    continue;
                }

                if (visual is null)
                {
                    var v = new StepLineVisualPoint<TDrawingContext, TVisual>();
                    visual = v;

                    if (isFirstDraw)
                    {
                        v.Geometry.X = secondaryScale.ToPixels(coordinate.SecondaryValue);
                        v.Geometry.Y = p;
                        v.Geometry.Width = 0;
                        v.Geometry.Height = 0;

                        v.StepSegment.Xi = secondaryScale.ToPixels(coordinate.SecondaryValue - ds);
                        v.StepSegment.Xj = secondaryScale.ToPixels(coordinate.SecondaryValue);
                        v.StepSegment.Yi = p;
                        v.StepSegment.Yj = p;
                    }

                    point.Context.Visual = v.Geometry;
                    point.Context.AdditionalVisuals = v;
                    OnPointCreated(point);
                }

                visual.Geometry.Opacity = 1;

                if (hasSvg)
                {
                    var svgVisual = (IVariableSvgPath<TDrawingContext>)visual.Geometry;
                    if (_geometrySvgChanged || svgVisual.SVGPath is null)
                        svgVisual.SVGPath = GeometrySvg ?? throw new Exception("svg path is not defined");
                }

                _ = everFetched.Add(point);

                GeometryFill?.AddGeometryToPaintTask(cartesianChart.Canvas, visual.Geometry);
                GeometryStroke?.AddGeometryToPaintTask(cartesianChart.Canvas, visual.Geometry);

                visual.StepSegment.Id = point.Context.Entity.MetaData!.EntityIndex;

                if (Fill is not null) fillVector!.AddConsecutiveSegment(visual.StepSegment, !isFirstDraw);
                if (Stroke is not null) strokeVector!.AddConsecutiveSegment(visual.StepSegment, !isFirstDraw);

                visual.StepSegment.Xi = secondaryScale.ToPixels(coordinate.SecondaryValue - ds);
                visual.StepSegment.Xj = secondaryScale.ToPixels(coordinate.SecondaryValue);
                visual.StepSegment.Yi = primaryScale.ToPixels(coordinate.PrimaryValue + s - dp);
                visual.StepSegment.Yj = primaryScale.ToPixels(coordinate.PrimaryValue + s);

                var x = secondaryScale.ToPixels(coordinate.SecondaryValue);
                var y = primaryScale.ToPixels(coordinate.PrimaryValue + s);

                visual.Geometry.MotionProperties[nameof(visual.Geometry.X)]
                    .CopyFrom(visual.StepSegment.MotionProperties[nameof(visual.StepSegment.Xj)]);
                visual.Geometry.MotionProperties[nameof(visual.Geometry.Y)]
                    .CopyFrom(visual.StepSegment.MotionProperties[nameof(visual.StepSegment.Yj)]);
                visual.Geometry.TranslateTransform = new LvcPoint(-hgs, -hgs);

                visual.Geometry.Width = gs;
                visual.Geometry.Height = gs;
                visual.Geometry.RemoveOnCompleted = false;

                visual.FillPath = fillVector!.AreaGeometry;
                visual.StrokePath = strokeVector!.AreaGeometry;

                var hags = gs < 8 ? 8 : gs;

                if (point.Context.HoverArea is not RectangleHoverArea ha)
                    point.Context.HoverArea = ha = new RectangleHoverArea();

                _ = ha
                    .SetDimensions(x - uwx * 0.5f, y - hgs, uwx, gs)
                    .CenterXToolTip();

                _ = coordinate.PrimaryValue >= pivot
                    ? ha.StartYToolTip()
                    : ha.EndYToolTip().IsLessThanPivot();

                pointsCleanup.Clean(point);

                if (DataLabelsPaint is not null)
                {
                    var label = (TLabel?)point.Context.Label;

                    if (label is null)
                    {
                        var l = new TLabel { X = x - hgs, Y = p - hgs, RotateTransform = (float)DataLabelsRotation, MaxWidth = (float)DataLabelsMaxWidth };
                        l.Animate(EasingFunction ?? cartesianChart.EasingFunction, AnimationsSpeed ?? cartesianChart.AnimationsSpeed);
                        label = l;
                        point.Context.Label = l;
                    }

                    DataLabelsPaint.AddGeometryToPaintTask(cartesianChart.Canvas, label);
                    label.Text = DataLabelsFormatter(new ChartPoint<TModel, TVisual, TLabel>(point));
                    label.TextSize = dls;
                    label.Padding = DataLabelsPadding;

                    if (isFirstDraw)
                        label.CompleteTransition(
                            nameof(label.TextSize), nameof(label.X), nameof(label.Y), nameof(label.RotateTransform));

                    var m = label.Measure(DataLabelsPaint);
                    var labelPosition = GetLabelPosition(
                        x - hgs, y - hgs, gs, gs, m, DataLabelsPosition,
                        SeriesProperties, coordinate.PrimaryValue > Pivot, drawLocation, drawMarginSize);
                    if (DataLabelsTranslate is not null) label.TranslateTransform =
                        new LvcPoint(m.Width * DataLabelsTranslate.Value.X, m.Height * DataLabelsTranslate.Value.Y);

                    label.X = labelPosition.X;
                    label.Y = labelPosition.Y;
                }

                OnPointMeasured(point);

                previousPrimary = coordinate.PrimaryValue + s;
                previousSecondary = coordinate.SecondaryValue;
            }

            strokeVector?.End();
            fillVector?.End();

            if (GeometryFill is not null)
            {
                cartesianChart.Canvas.AddDrawableTask(GeometryFill);
                GeometryFill.SetClipRectangle(cartesianChart.Canvas, clipping);
                GeometryFill.ZIndex = actualZIndex + 0.3;
            }
            if (GeometryStroke is not null)
            {
                cartesianChart.Canvas.AddDrawableTask(GeometryStroke);
                GeometryStroke.SetClipRectangle(cartesianChart.Canvas, clipping);
                GeometryStroke.ZIndex = actualZIndex + 0.4;
            }

            if (!isSegmentEmpty) segmentI++;
        }

        var maxSegment = fillPathHelperContainer.Count > strokePathHelperContainer.Count
            ? fillPathHelperContainer.Count
            : strokePathHelperContainer.Count;

        for (var i = maxSegment - 1; i >= segmentI; i--)
        {
            if (i < fillPathHelperContainer.Count)
            {
                var segmentFill = fillPathHelperContainer[i];
                Fill?.RemoveGeometryFromPainTask(cartesianChart.Canvas, segmentFill);
                segmentFill.Commands.Clear();
                fillPathHelperContainer.RemoveAt(i);
            }

            if (i < strokePathHelperContainer.Count)
            {
                var segmentStroke = strokePathHelperContainer[i];
                Stroke?.RemoveGeometryFromPainTask(cartesianChart.Canvas, segmentStroke);
                segmentStroke.Commands.Clear();
                strokePathHelperContainer.RemoveAt(i);
            }
        }

        if (DataLabelsPaint is not null)
        {
            cartesianChart.Canvas.AddDrawableTask(DataLabelsPaint);
            DataLabelsPaint.SetClipRectangle(cartesianChart.Canvas, clipping);
            DataLabelsPaint.ZIndex = actualZIndex + 0.5;
        }

        pointsCleanup.CollectPoints(
            everFetched, cartesianChart.View, primaryScale, secondaryScale, SoftDeleteOrDisposePoint);

        _geometrySvgChanged = false;
    }

    /// <inheritdoc cref="GetRequestedGeometrySize"/>
    protected override double GetRequestedGeometrySize()
    {
        return (GeometrySize + (GeometryStroke?.StrokeThickness ?? 0)) * 0.5f;
    }

    /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.GetMiniaturesSketch"/>
    [Obsolete]
    public override Sketch<TDrawingContext> GetMiniaturesSketch()
    {
        var schedules = new List<PaintSchedule<TDrawingContext>>();

        if (GeometryFill is not null) schedules.Add(BuildMiniatureSchedule(GeometryFill, new TVisual()));
        else if (Fill is not null) schedules.Add(BuildMiniatureSchedule(Fill, new TVisual()));

        if (GeometryStroke is not null) schedules.Add(BuildMiniatureSchedule(GeometryStroke, new TVisual()));
        else if (Stroke is not null) schedules.Add(BuildMiniatureSchedule(Stroke, new TVisual()));

        return new Sketch<TDrawingContext>(MiniatureShapeSize, MiniatureShapeSize, GeometrySvg)
        {
            PaintSchedules = schedules
        };
    }

    /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.GetMiniature"/>"/>
    public override VisualElement<TDrawingContext> GetMiniature(ChartPoint? point, int zindex)
    {
        var noGeometryPaint = GeometryStroke is null && GeometryFill is null;
        var usesLine = (GeometrySize < 1 || noGeometryPaint) && Stroke is not null;

        var typedPoint = point is null ? null : ConvertToTypedChartPoint(point);

        return usesLine
            ? new LineVisual<TLineGeometry, TDrawingContext>
            {
                Stroke = GetMiniaturePaint(Stroke, zindex + 2),
                Width = MiniatureShapeSize,
                Height = 0,
                ClippingMode = ClipMode.None
            }
            : new GeometryVisual<TVisual, TLabel, TDrawingContext>
            {
                Fill = GetMiniatureFill(point, zindex + 1),
                Stroke = GetMiniatureStroke(point, zindex + 2),
                Width = MiniatureShapeSize,
                Height = MiniatureShapeSize,
                Rotation = typedPoint?.Visual?.RotateTransform ?? 0,
                Svg = GeometrySvg,
                ClippingMode = ClipMode.None
            };
    }

    /// <inheritdoc cref="GetMiniatureFill(ChartPoint?, int)"/>
    protected override IPaint<TDrawingContext>? GetMiniatureFill(ChartPoint? point, int zIndex)
    {
        var p = point is null ? null : ConvertToTypedChartPoint(point);
        var paint = p?.Visual?.Fill ?? GeometryFill;

        return GetMiniaturePaint(paint, zIndex);
    }

    /// <inheritdoc cref="GetMiniatureStroke(ChartPoint?, int)"/>
    protected override IPaint<TDrawingContext>? GetMiniatureStroke(ChartPoint? point, int zIndex)
    {
        var p = point is null ? null : ConvertToTypedChartPoint(point);
        var paint = p?.Visual?.Fill ?? GeometryStroke;

        return GetMiniaturePaint(paint, zIndex);
    }

    /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.OnPointerEnter(ChartPoint)"/>
    protected override void OnPointerEnter(ChartPoint point)
    {
        var visual = (TVisual?)point.Context.Visual;
        if (visual is null) return;
        visual.ScaleTransform = new LvcPoint(1.3f, 1.3f);

        base.OnPointerEnter(point);
    }

    /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.OnPointerLeft(ChartPoint)"/>
    protected override void OnPointerLeft(ChartPoint point)
    {
        var visual = (TVisual?)point.Context.Visual;
        if (visual is null) return;
        visual.ScaleTransform = new LvcPoint(1f, 1f);

        base.OnPointerLeft(point);
    }

    /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.SetDefaultPointTransitions(ChartPoint)"/>
    protected override void SetDefaultPointTransitions(ChartPoint chartPoint)
    {
        var chart = chartPoint.Context.Chart;

        if (chartPoint.Context.AdditionalVisuals is not StepLineVisualPoint<TDrawingContext, TVisual> visual)
            throw new Exception("Unable to initialize the point instance.");

        visual.Geometry.Animate(EasingFunction ?? chart.EasingFunction, AnimationsSpeed ?? chart.AnimationsSpeed);
        visual.StepSegment.Animate(EasingFunction ?? chart.EasingFunction, AnimationsSpeed ?? chart.AnimationsSpeed);
    }

    /// <inheritdoc cref="CartesianSeries{TModel, TVisual, TLabel, TDrawingContext}.SoftDeleteOrDisposePoint(ChartPoint, Scaler, Scaler)"/>
    protected internal override void SoftDeleteOrDisposePoint(ChartPoint point, Scaler primaryScale, Scaler secondaryScale)
    {
        var visual = (StepLineVisualPoint<TDrawingContext, TVisual>?)point.Context.AdditionalVisuals;
        if (visual is null) return;
        if (DataFactory is null) throw new Exception("Data provider not found");

        var chartView = (ICartesianChartView<TDrawingContext>)point.Context.Chart;
        if (chartView.Core.IsZoomingOrPanning)
        {
            visual.Geometry.CompleteTransition(null);
            visual.Geometry.RemoveOnCompleted = true;
            DataFactory.DisposePoint(point);
            return;
        }

        var coordinate = point.Coordinate;

        var x = secondaryScale.ToPixels(coordinate.SecondaryValue);
        var y = primaryScale.ToPixels(coordinate.PrimaryValue);

        visual.Geometry.X = x + visual.Geometry.Width * 0.5f;
        visual.Geometry.Y = y + visual.Geometry.Height * 0.5f;
        visual.Geometry.Height = 0;
        visual.Geometry.Width = 0;
        visual.Geometry.Opacity = 0;
        visual.Geometry.RemoveOnCompleted = true;

        DataFactory.DisposePoint(point);

        var label = (TLabel?)point.Context.Label;
        if (label is null) return;

        label.RemoveOnCompleted = true;
    }

    /// <inheritdoc cref="Series{TModel, TVisual, TLabel, TDrawingContext}.SoftDeleteOrDispose(IChartView)"/>
    public override void SoftDeleteOrDispose(IChartView chart)
    {
        base.SoftDeleteOrDispose(chart);
        var canvas = ((ICartesianChartView<TDrawingContext>)chart).CoreCanvas;

        if (Fill is not null)
        {
            foreach (var activeChartContainer in _fillPathHelperDictionary.ToArray())
                foreach (var pathHelper in activeChartContainer.Value.ToArray())
                    Fill.RemoveGeometryFromPainTask(canvas, pathHelper);
        }

        if (Stroke is not null)
        {
            foreach (var activeChartContainer in _strokePathHelperDictionary.ToArray())
                foreach (var pathHelper in activeChartContainer.Value.ToArray())
                    Stroke.RemoveGeometryFromPainTask(canvas, pathHelper);
        }

        if (GeometryFill is not null) canvas.RemovePaintTask(GeometryFill);
        if (GeometryStroke is not null) canvas.RemovePaintTask(GeometryStroke);
    }

    /// <inheritdoc/>
    public override void RemoveFromUI(Chart<TDrawingContext> chart)
    {
        base.RemoveFromUI(chart);

        _ = _fillPathHelperDictionary.Remove(chart.Canvas.Sync);
        _ = _strokePathHelperDictionary.Remove(chart.Canvas.Sync);
    }

    /// <summary>
    /// Gets the paint tasks.
    /// </summary>
    /// <returns></returns>
    protected internal override IPaint<TDrawingContext>?[] GetPaintTasks()
    {
        return [Stroke, Fill, _geometryFill, _geometryStroke, DataLabelsPaint];
    }

    private void DeleteNullPoint(ChartPoint point, Scaler xScale, Scaler yScale)
    {
        if (point.Context.Visual is not StepLineVisualPoint<TDrawingContext, TVisual> visual) return;

        var coordinate = point.Coordinate;

        var x = xScale.ToPixels(coordinate.SecondaryValue);
        var y = yScale.ToPixels(coordinate.PrimaryValue);
        var gs = _geometrySize;
        var hgs = gs / 2f;

        visual.Geometry.X = x - hgs;
        visual.Geometry.Y = y - hgs;
        visual.Geometry.Width = gs;
        visual.Geometry.Height = gs;
        visual.Geometry.RemoveOnCompleted = true;
        point.Context.Visual = null;
    }

    private SegmentVisual GetSegmentVisual(int index, List<TPathGeometry> container, VectorClosingMethod method)
    {
        var isNew = false;
        TPathGeometry? path;

        if (index >= container.Count)
        {
            isNew = true;
            path = new TPathGeometry { ClosingMethod = method };
            container.Add(path);
        }
        else
        {
            path = container[index];
        }

        return new SegmentVisual(isNew, path);
    }

    private class SegmentVisual(bool isNew, TPathGeometry path)
    {
        public bool IsNew { get; set; } = isNew;

        public TPathGeometry Path { get; set; } = path;
    }

    private static SeriesProperties GetProperties(bool isStacked)
    {
        return SeriesProperties.StepLine | SeriesProperties.PrimaryAxisVerticalOrientation |
            SeriesProperties.Sketch | SeriesProperties.PrefersXStrategyTooltips |
            (isStacked ? SeriesProperties.Stacked : 0);
    }
}
