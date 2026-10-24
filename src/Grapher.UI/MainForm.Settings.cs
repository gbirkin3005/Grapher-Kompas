using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Grapher.Core.Build;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Grapher.UI.Controls;

namespace Grapher.UI;

// Настройки: дерево элементов диаграммы, страницы параметров и их связь с проектом.
public sealed partial class MainForm
{
    private enum PageKind
    {
        Diagram,
        Scales,
        Scale,
        PolarAngle,
        PolarRadius,
        Ternary,
        Curves,
        Curve,
        Design,
        Placement
    }

    /// <summary>Узел дерева: какая страница и для какой шкалы или кривой.</summary>
    private sealed class NodeTag
    {
        public readonly PageKind Page;

        /// <summary>Шкала: −1 — X, 0 — основная Y, k — k-я дополнительная Y. Кривая — её номер.</summary>
        public readonly int Index;

        public NodeTag(PageKind page, int index = 0)
        {
            Page = page;
            Index = index;
        }

        public bool Same(NodeTag other) => other != null && other.Page == Page && other.Index == Index;
    }

    private readonly Dictionary<PageKind, Control> _pages = new Dictionary<PageKind, Control>();
    private PageKind _currentPage = PageKind.Diagram;
    private bool _rebuildingTree;

    /// <summary>Шкала, показанная на странице шкалы: −1 — X, 0 — Y, k — дополнительная.</summary>
    private int _axisIndex = -1;

    private DiagramKind Kind => (DiagramKind)Math.Max(0, _kind.SelectedIndex);

    // ------------------------------------------------------------------ дерево

    /// <summary>Открывает страницу настроек: 0 — «Диаграмма», 1 — первая шкала, 2 — первая кривая, 3 — «Оформление», 4 — «Размещение».</summary>
    public void SelectSettingsTab(int index)
    {
        switch (index)
        {
            case 0: SelectNode(new NodeTag(PageKind.Diagram)); break;
            case 1:
                SelectNode(Kind == DiagramKind.Polar ? new NodeTag(PageKind.PolarAngle)
                    : Kind == DiagramKind.Ternary ? new NodeTag(PageKind.Ternary) : new NodeTag(PageKind.Scale, -1));
                break;
            case 2: SelectNode(new NodeTag(PageKind.Curve, 0)); break;
            case 3: SelectNode(new NodeTag(PageKind.Design)); break;
            case 4: SelectNode(new NodeTag(PageKind.Placement)); break;
        }
    }

    private void RebuildTree()
    {
        var selected = _tree.SelectedNode?.Tag as NodeTag;
        _rebuildingTree = true;
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            TreeNode Add(TreeNodeCollection parent, string text, NodeTag tag)
            {
                var node = new TreeNode(text) { Tag = tag };
                parent.Add(node);
                return node;
            }

            Add(_tree.Nodes, "Диаграмма", new NodeTag(PageKind.Diagram));
            TreeNode scales = Add(_tree.Nodes, "Шкалы", new NodeTag(PageKind.Scales));
            switch (Kind)
            {
                case DiagramKind.Polar:
                    Add(scales.Nodes, "Угловая шкала", new NodeTag(PageKind.PolarAngle));
                    Add(scales.Nodes, RadiusNodeText(), new NodeTag(PageKind.PolarRadius));
                    break;
                case DiagramKind.Ternary:
                    Add(scales.Nodes, "Шкалы A, B, C", new NodeTag(PageKind.Ternary));
                    break;
                default:
                    for (int i = -1; i <= _project.ExtraAxes.Count; i++)
                    {
                        Add(scales.Nodes, AxisNodeText(i), new NodeTag(PageKind.Scale, i));
                    }
                    break;
            }

            TreeNode curves = Add(_tree.Nodes, "Кривые", new NodeTag(PageKind.Curves));
            for (int i = 0; i < _project.Curves.Count; i++)
            {
                Add(curves.Nodes, CurveTitle(i), new NodeTag(PageKind.Curve, i));
            }
            Add(_tree.Nodes, "Оформление", new NodeTag(PageKind.Design));
            Add(_tree.Nodes, "Размещение", new NodeTag(PageKind.Placement));
            _tree.ExpandAll();

            _tree.SelectedNode = FindNode(selected) ?? _tree.Nodes[0];
        }
        finally
        {
            _tree.EndUpdate();
            _rebuildingTree = false;
        }
        ShowNode(_tree.SelectedNode);
    }

    private TreeNode FindNode(NodeTag tag)
    {
        if (tag == null) return null;
        TreeNode Search(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (tag.Same(node.Tag as NodeTag)) return node;
                TreeNode found = Search(node.Nodes);
                if (found != null) return found;
            }
            return null;
        }
        return Search(_tree.Nodes);
    }

    private void SelectNode(NodeTag tag)
    {
        TreeNode node = FindNode(tag);
        if (node != null) _tree.SelectedNode = node;
    }

    /// <summary>Обновляет подписи узлов шкал и кривых после переименования.</summary>
    private void UpdateTreeTexts()
    {
        void Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is NodeTag tag)
                {
                    string text = null;
                    if (tag.Page == PageKind.Scale && AxisAt(tag.Index) != null) text = AxisNodeText(tag.Index);
                    else if (tag.Page == PageKind.Curve && tag.Index < _project.Curves.Count) text = CurveTitle(tag.Index);
                    else if (tag.Page == PageKind.PolarRadius) text = RadiusNodeText();
                    if (text != null && node.Text != text) node.Text = text;
                }
                Walk(node.Nodes);
            }
        }
        Walk(_tree.Nodes);
        if (_tree.SelectedNode != null) _pageTitle.Text = PageTitle(_tree.SelectedNode);
    }

    private void ShowNode(TreeNode node)
    {
        if (!(node?.Tag is NodeTag tag)) return;

        switch (tag.Page)
        {
            case PageKind.Scale:
                _axisIndex = tag.Index;
                ShowAxisPage();
                break;
            case PageKind.Curve:
                _selectedCurve = tag.Index;
                ShowCurve();
                break;
        }

        _currentPage = tag.Page;
        _pageHost.SuspendLayout();
        foreach (KeyValuePair<PageKind, Control> page in _pages)
        {
            page.Value.Visible = page.Key == tag.Page;
        }
        _pageHost.ResumeLayout();
        FitPageScroll();
        _pageHost.AutoScrollPosition = new Point(0, 0);
        _pageTitle.Text = PageTitle(node);
        UpdateEnabledState();
    }

    private static string PageTitle(TreeNode node) =>
        node.Parent != null && node.Parent.Parent == null ? node.Parent.Text + " › " + node.Text : node.Text;

    private string AxisNodeText(int index)
    {
        AxisSettings axis = AxisAt(index);
        string name = index < 0 ? "Ось X" : index == 0 ? "Ось Y" : "Шкала Y" + (index + 1).ToString(CultureInfo.CurrentCulture);
        if (axis == null) return name;
        string title = TextMarkup.Plain(axis.Title ?? "").Trim();
        if (axis.Logarithmic) name += " (лог.)";
        return title.Length == 0 ? name : name + " — " + title;
    }

    private string RadiusNodeText()
    {
        string title = TextMarkup.Plain(_project.YAxis.Title ?? "").Trim();
        return title.Length == 0 ? "Радиальная шкала" : "Радиальная шкала — " + title;
    }

    private string CurveTitle(int index)
    {
        string name = TextMarkup.Plain(_project.Curves[index].Name ?? "");
        return (index + 1).ToString(CultureInfo.CurrentCulture) + ". " + (string.IsNullOrWhiteSpace(name) ? "(без названия)" : name);
    }

    // ------------------------------------------------------------------ события

    private void WireEvents()
    {
        _buildButton.Click += (s, e) => BuildInKompas();

        _grid.DataChanged += (s, e) =>
        {
            MarkDirty();
            SchedulePreview();
        };
        _grid.StructureChanged += (s, e) => OnCurvesChanged();
        _grid.CurveColumnSelected += index => SelectNode(new NodeTag(PageKind.Curve, index));
        _issuesList.DoubleClick += (s, e) =>
        {
            if (_issuesList.SelectedItem is IssueItem item && item.Issue.Row > 0)
            {
                _grid.GoToCell(item.Issue.Row, item.Issue.Column);
            }
        };

        _tree.AfterSelect += (s, e) =>
        {
            if (!_rebuildingTree) ShowNode(e.Node);
        };
        _kind.SelectedIndexChanged += (s, e) => OnKindChanged();

        // Общие настройки: любое изменение переносится в проект и обновляет предпросмотр.
        EventHandler changed = (s, e) => OnSettingChanged();
        foreach (CheckBox box in new[]
                 {
                     _equalScale, _gridOn, _axesOn, _arrowsOn, _frameOn, _separateArrows, _nativeArrows, _labelsOn, _italic,
                     _scaleFactors, _breakGrid, _dashedJumps, _smoothPeriodic, _legendOn, _decimateOn, _radAuto
                 })
        {
            box.CheckedChanged += changed;
        }
        foreach (RadioButton radio in new[] { _polarCcw, _polarCw, _pickPoint, _useCoords })
        {
            radio.CheckedChanged += changed;
        }
        foreach (NumberBox box in new[]
                 {
                     _angle, _axisExtension, _tickLength, _arrowLength, _polarZero, _polarStep, _radMin, _radMax, _radStep,
                     _ternTotal, _ternStep, _labelHeight, _titleHeight, _placeX, _placeY, _decimateThreshold, _decimateTolerance
                 })
        {
            box.ValueChanged += changed;
        }
        foreach (ComboBox box in new[]
                 {
                     _crossing, _polarUnit, _polarRange, _polarDegree, _radDecimals, _radSide, _titlePlacement, _units, _decimalSeparator
                 })
        {
            box.SelectedIndexChanged += changed;
        }
        foreach (TextBox box in new[] { _radTitle, _radUnit, _ternA, _ternB, _ternC, _ternUnit })
        {
            box.TextChanged += changed;
        }
        _fontName.TextChanged += changed;
        _radRay.TextChanged += changed;
        _radEvery.ValueChanged += changed;
        _ternEvery.ValueChanged += changed;
        _radTitle.TextChanged += (s, e) => UpdateTreeTexts();
        _gridMode.SelectedIndexChanged += (s, e) => OnGridModeChanged();
        _polarZeroPreset.SelectedIndexChanged += (s, e) => OnZeroPresetChanged();
        _polarStep.ValueChanged += (s, e) => FillRayList();

        // Габарит и масштабный коэффициент связаны в обе стороны.
        _width.ValueChanged += (s, e) => OnSizeEdited(SizeDriver.Width, _width);
        _height.ValueChanged += (s, e) => OnSizeEdited(SizeDriver.Height, _height);
        _axScale.ValueChanged += (s, e) =>
        {
            if (_axisIndex < 0) OnSizeEdited(SizeDriver.ScaleX, _axScale);
            else if (_axisIndex == 0) OnSizeEdited(SizeDriver.ScaleY, _axScale);
        };

        // Шкала прямоугольной диаграммы.
        EventHandler axisChanged = (s, e) => OnAxisPageChanged();
        foreach (TextBox box in new[] { _axTitle, _axUnit }) box.TextChanged += axisChanged;
        foreach (ComboBox box in new[] { _axSide, _axDecimals }) box.SelectedIndexChanged += axisChanged;
        foreach (CheckBox box in new[] { _axAuto, _axAlign, _axPi }) box.CheckedChanged += axisChanged;
        foreach (NumberBox box in new[] { _axMin, _axMax, _axStep }) box.ValueChanged += axisChanged;
        _axLog.CheckedChanged += axisChanged;
        _axEvery.ValueChanged += axisChanged;
        _scalesAdd.Click += (s, e) => AddExtra();
        _axRemove.Click += (s, e) => RemoveExtra();

        // Кривая.
        _curvesAdd.Click += (s, e) => AddCurve();
        _curveAdd.Click += (s, e) => AddCurve();
        _curveRemove.Click += (s, e) => RemoveCurve();
        EventHandler curveChanged = (s, e) => OnCurveSettingChanged();
        foreach (TextBox box in new[] { _curveName, _curveLabelText }) box.TextChanged += curveChanged;
        foreach (CheckBox box in new[] { _curveVisible, _curveLabelOn, _curveLabelAuto }) box.CheckedChanged += curveChanged;
        foreach (ComboBox box in new[] { _curveAxis, _curveMode, _curveMarker, _curveMarkerPlacement, _curveLabelDir }) box.SelectedIndexChanged += curveChanged;
        _curveStyle.SelectedIndexChanged += curveChanged;
        _curveMarkerSize.ValueChanged += curveChanged;
        _curveLabelPos.ValueChanged += curveChanged;
        _curveMarkerEvery.ValueChanged += curveChanged;
        _curveColor.Click += (s, e) => ChooseCurveColor();
        _curveColorReset.Click += (s, e) => SetCurveColor(null);
    }

    private void OnSettingChanged()
    {
        if (_loading) return;
        ReadUi();
        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Завершает редактирование ячейки таблицы, чтобы введённое значение попало в данные.</summary>
    private void CommitEdits()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
    }

    // ------------------------------------------------------------------ проект → окно

    private void WriteUi()
    {
        _loading = true;
        try
        {
            GraphSettings g = _project.Graph;
            _kind.SelectedIndex = Math.Max(0, Math.Min(_kind.Items.Count - 1, (int)g.Kind));

            // «Диаграмма»
            _width.SetValue(g.WidthMm);
            _height.SetValue(g.HeightMm);
            _equalScale.Checked = g.EqualScale;
            _angle.SetValue(_project.Placement.AngleDeg);
            _gridOn.Checked = g.ShowGrid;
            _gridMode.SelectedIndex = Math.Max(0, Math.Min(3, (int)g.GridMode));
            _axesOn.Checked = g.ShowAxes;
            _arrowsOn.Checked = g.ShowArrows;
            _frameOn.Checked = g.ShowFrame;
            _crossing.SelectedIndex = (int)g.Crossing;
            _axisExtension.SetValue(g.AxisExtensionMm);
            _tickLength.SetValue(g.TickLengthMm);
            _arrowLength.SetValue(g.ArrowLengthMm);

            // Полярная диаграмма
            PolarSettings polar = _project.Polar;
            _polarUnit.SelectedIndex = polar.AngleUnit == AngleUnit.Radians ? 1 : 0;
            ShowZero(polar.ZeroAngleDeg);
            _polarCcw.Checked = !polar.Clockwise;
            _polarCw.Checked = polar.Clockwise;
            _polarStep.SetValue(polar.AngleStepDeg);
            _polarRange.SelectedIndex = polar.SignedAngles ? 1 : 0;
            _polarDegree.SelectedIndex = polar.DegreeSignOnAll ? 0 : 1;
            AxisSettings radial = _project.YAxis;
            _radTitle.Text = radial.Title;
            _radUnit.Text = radial.Unit;
            _radAuto.Checked = radial.AutoLimits;
            _radMin.SetValue(radial.Min);
            _radMax.SetValue(radial.Max);
            _radStep.SetValue(radial.GridStep);
            _radEvery.Value = Math.Max(1, Math.Min(100, radial.LabelEvery));
            _radDecimals.SelectedIndex = Math.Max(0, Math.Min(7, radial.Decimals + 1));
            FillRayList();
            _radRay.Text = NumberFormatter.ForInput(polar.LabelRayDeg, 3);
            _radSide.SelectedIndex = polar.LabelSide == RadialLabelSide.Left ? 1 : 0;

            // Треугольная диаграмма
            TernarySettings ternary = _project.Ternary;
            _ternA.Text = _project.XAxis.Title;
            _ternB.Text = _project.YAxis.Title;
            _ternC.Text = ternary.TitleC;
            _ternTotal.SetValue(ternary.Total);
            _ternUnit.Text = ternary.Unit;
            _ternStep.SetValue(ternary.GridStep);
            _ternEvery.Value = Math.Max(1, Math.Min(100, ternary.LabelEvery));

            // «Оформление»
            _titlePlacement.SelectedIndex = (int)g.TitlePlacement;
            _separateArrows.Checked = g.SeparateArrows;
            _units.SelectedIndex = Math.Max(0, Math.Min(_units.Items.Count - 1, (int)g.Units));
            _nativeArrows.Checked = g.NativeArrows;
            _labelsOn.Checked = g.ShowTickLabels;
            _decimalSeparator.SelectedIndex = g.DecimalComma ? 0 : 1;
            _fontName.Text = g.FontName;
            _italic.Checked = g.Italic;
            _labelHeight.SetValue(g.LabelHeightMm);
            _titleHeight.SetValue(g.TitleHeightMm);
            _scaleFactors.Checked = g.ShowScaleFactors;
            _breakGrid.Checked = g.BreakGridUnderLabels;
            _dashedJumps.Checked = g.DashedJumps;
            _smoothPeriodic.Checked = g.SmoothPeriodic;
            _legendOn.Checked = g.ShowLegend;

            // «Размещение»
            _pickPoint.Checked = _project.Placement.PickPoint;
            _useCoords.Checked = !_project.Placement.PickPoint;
            _placeX.SetValue(_project.Placement.X);
            _placeY.SetValue(_project.Placement.Y);
            _decimateOn.Checked = g.Decimate;
            _decimateThreshold.SetValue(g.DecimateThreshold);
            _decimateTolerance.SetValue(g.DecimateToleranceMm);
        }
        finally
        {
            _loading = false;
        }

        ApplyKind();
        RebuildTree();
        UpdateStepUnits();
        UpdateEnabledState();
    }

    // ------------------------------------------------------------------ окно → проект

    private void ReadUi()
    {
        GraphSettings g = _project.Graph;
        DiagramKind kind = g.Kind = Kind;

        g.EqualScale = _equalScale.Checked;
        _project.Placement.AngleDeg = _angle.Value;
        g.ShowGrid = _gridOn.Checked;
        g.GridMode = (GridStepMode)Math.Max(0, _gridMode.SelectedIndex);
        g.ShowAxes = _axesOn.Checked;
        g.ShowArrows = _arrowsOn.Checked;
        g.ShowFrame = _frameOn.Checked;
        g.Crossing = (AxesCrossing)Math.Max(0, _crossing.SelectedIndex);
        g.AxisExtensionMm = _axisExtension.Value;
        g.TickLengthMm = _tickLength.Value;
        g.ArrowLengthMm = _arrowLength.Value;

        PolarSettings polar = _project.Polar;
        polar.AngleUnit = _polarUnit.SelectedIndex == 1 ? AngleUnit.Radians : AngleUnit.Degrees;
        polar.ZeroAngleDeg = _polarZero.Value;
        polar.Clockwise = _polarCw.Checked;
        polar.AngleStepDeg = _polarStep.Value;
        polar.SignedAngles = _polarRange.SelectedIndex == 1;
        polar.DegreeSignOnAll = _polarDegree.SelectedIndex != 1;
        polar.LabelSide = _radSide.SelectedIndex == 1 ? RadialLabelSide.Left : RadialLabelSide.Right;
        if (NumberParser.Parse(_radRay.Text, out double ray) == NumberParseStatus.Ok) polar.LabelRayDeg = ray;

        // Поля, которые при другом виде диаграммы показывают те же величины, читаются только для своего вида.
        if (kind == DiagramKind.Polar)
        {
            AxisSettings radial = _project.YAxis;
            radial.Title = _radTitle.Text;
            radial.Unit = _radUnit.Text;
            radial.AutoLimits = _radAuto.Checked;
            if (!radial.AutoLimits)
            {
                radial.Min = _radMin.Value;
                radial.Max = _radMax.Value;
            }
            if (g.GridMode != GridStepMode.Auto) radial.GridStep = _radStep.Value;
            radial.LabelEvery = (int)_radEvery.Value;
            radial.Decimals = _radDecimals.SelectedIndex - 1;
        }
        if (kind == DiagramKind.Ternary)
        {
            _project.XAxis.Title = _ternA.Text;
            _project.YAxis.Title = _ternB.Text;
            TernarySettings ternary = _project.Ternary;
            ternary.TitleC = _ternC.Text;
            ternary.Total = _ternTotal.Value;
            ternary.Unit = _ternUnit.Text;
            ternary.GridStep = _ternStep.Value;
            ternary.LabelEvery = (int)_ternEvery.Value;
        }

        g.TitlePlacement = (AxisTitlePlacement)Math.Max(0, _titlePlacement.SelectedIndex);
        g.SeparateArrows = _separateArrows.Checked;
        g.Units = (UnitPlacement)Math.Max(0, _units.SelectedIndex);
        g.NativeArrows = _nativeArrows.Checked;
        g.ShowTickLabels = _labelsOn.Checked;
        g.DecimalComma = _decimalSeparator.SelectedIndex != 1;
        if (!string.IsNullOrWhiteSpace(_fontName.Text)) g.FontName = _fontName.Text.Trim();
        g.Italic = _italic.Checked;
        g.LabelHeightMm = _labelHeight.Value;
        g.TitleHeightMm = _titleHeight.Value;
        g.ShowScaleFactors = _scaleFactors.Checked;
        g.BreakGridUnderLabels = _breakGrid.Checked;
        g.DashedJumps = _dashedJumps.Checked;
        g.SmoothPeriodic = _smoothPeriodic.Checked;
        g.ShowLegend = _legendOn.Checked;

        _project.Placement.PickPoint = _pickPoint.Checked;
        _project.Placement.X = _placeX.Value;
        _project.Placement.Y = _placeY.Value;
        g.Decimate = _decimateOn.Checked;
        g.DecimateThreshold = (int)Math.Max(2, Math.Min(int.MaxValue, _decimateThreshold.Value));
        g.DecimateToleranceMm = _decimateTolerance.Value;
    }

    // ------------------------------------------------------------------ доступность и подписи

    private void UpdateEnabledState()
    {
        DiagramKind kind = Kind;
        bool cartesian = kind == DiagramKind.Cartesian;
        bool autoStep = _gridMode.SelectedIndex <= 0;

        _arrowsOn.Enabled = _axesOn.Checked;
        _axisExtension.Enabled = _axesOn.Checked && _arrowsOn.Checked;
        _arrowLength.Enabled = !_nativeArrows.Checked;

        // Шкала прямоугольной диаграммы
        bool extra = _axisIndex > 0, log = _axLog.Checked;
        _axMin.Enabled = _axMax.Enabled = !_axAuto.Checked;
        _axAlign.Enabled = extra && _axAuto.Checked && !log;
        _axStep.Enabled = !autoStep && !log;
        _axDecimals.Enabled = _axPi.Enabled = !log && _labelsOn.Checked;
        _axScale.Enabled = !extra;
        _axRemove.Visible = extra;
        _scalesAdd.Visible = cartesian;

        // Полярная
        _radMin.Enabled = _radMax.Enabled = !_radAuto.Checked;
        _radStep.Enabled = !autoStep;
        _polarZero.Enabled = _polarZeroPreset.SelectedIndex == 4;

        // Оформление
        _separateArrows.Enabled = cartesian && _titlePlacement.SelectedIndex == 1;
        _decimalSeparator.Enabled = _labelsOn.Checked;

        // Размещение
        _placeX.Enabled = _placeY.Enabled = _useCoords.Checked;
        _decimateThreshold.Enabled = _decimateTolerance.Enabled = _decimateOn.Checked;

        // Кривая
        MarkerShape marker = SelectedMarker();
        _curveRemove.Enabled = _project.Curves.Count > 1;
        _removeCurveMenuItem.Enabled = _project.Curves.Count > 1;
        _curveMarkerSize.Enabled = marker != MarkerShape.None && !MarkerShapes.IsKompasPoint(marker);
        _curveMarkerPlacement.Enabled = marker != MarkerShape.None && cartesian;
        _curveMarkerEvery.Enabled = marker != MarkerShape.None && (_curveMarkerPlacement.SelectedIndex != 1 || !cartesian);
        _curveColorReset.Enabled = _curveColor.Value.HasValue;
        _curveAxis.Enabled = cartesian && _project.ExtraAxes.Count > 0;
        _curveLabelText.Enabled = _curveLabelAuto.Enabled = _curveLabelDir.Enabled = _curveLabelOn.Checked;
        _curveLabelPos.Enabled = _curveLabelOn.Checked && !_curveLabelAuto.Checked;
    }

    private static string InsertionPointName(DiagramKind kind)
    {
        switch (kind)
        {
            case DiagramKind.Polar:
                return "полюс (центр) диаграммы";
            case DiagramKind.Ternary:
                return "левая нижняя вершина треугольника";
            default:
                return "левый нижний угол поля графика";
        }
    }

    private string PickPrompt() => "Укажите точку вставки: " + InsertionPointName(_project.Graph.Kind);

    /// <summary>Подписи и состав настроек, зависящие от вида диаграммы.</summary>
    private void ApplyKind()
    {
        DiagramKind kind = Kind;
        bool cartesian = kind == DiagramKind.Cartesian, polar = kind == DiagramKind.Polar, ternary = kind == DiagramKind.Ternary;

        _kindHint.Text = polar ? "Столбцы таблицы: угол и радиусы кривых."
            : ternary ? "Столбцы таблицы: A и B каждой кривой; C = сумма − A − B."
            : "Столбцы таблицы: X и значения Y кривых.";

        _fWidth.Text = polar ? "Диаметр диаграммы, мм" : ternary ? "Сторона треугольника, мм" : "Ширина поля (по оси X), мм";
        _fHeight.Visible = cartesian;
        _fGridMode.Visible = !ternary;
        _axesSection.Visible = cartesian;

        _scalesHint.Text = polar
            ? "Полярная диаграмма: шкала углов (начало отсчёта, направление, числа) и шкала радиуса."
            : ternary
                ? "Треугольная диаграмма: три шкалы по сторонам треугольника, сумма составляющих постоянна."
                : "Шкала X и одна или несколько шкал Y. Дополнительные шкалы Y ставятся слева или справа от поля " +
                  "(Р 50-77-88, п. 2.4); кривая выбирает шкалу в своих параметрах.";

        _fTitlePlacement.Visible = cartesian;
        _fNativeArrows.Visible = !polar;
        _scaleFactors.Visible = _dashedJumps.Visible = _smoothPeriodic.Visible = cartesian;
        _fCurveAxis.Visible = cartesian;
        _placementGroup.Text = "Точка вставки — " + InsertionPointName(kind);
    }

    private void OnKindChanged()
    {
        if (_loading) return;
        ReadUi();
        // Поля пределов радиуса и обозначений составляющих показывают величины проекта для нового вида.
        _loading = true;
        try
        {
            _radTitle.Text = _project.YAxis.Title;
            _radUnit.Text = _project.YAxis.Unit;
            _radAuto.Checked = _project.YAxis.AutoLimits;
            _radMin.SetValue(_project.YAxis.Min);
            _radMax.SetValue(_project.YAxis.Max);
            _radStep.SetValue(_project.YAxis.GridStep);
            _ternA.Text = _project.XAxis.Title;
            _ternB.Text = _project.YAxis.Title;
            _height.SetValue(_project.Graph.HeightMm);
        }
        finally
        {
            _loading = false;
        }

        ApplyKind();
        RebuildTree();
        FitSettings(true);
        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    // ------------------------------------------------------------------ полярная диаграмма

    private static readonly double[] ZeroPresets = { 0, 90, 180, 270 };

    private void ShowZero(double angle)
    {
        double normalized = ((angle % 360) + 360) % 360;
        int preset = Array.FindIndex(ZeroPresets, a => Math.Abs(a - normalized) < 1e-9);
        _polarZeroPreset.SelectedIndex = preset >= 0 ? preset : 4;
        _polarZero.SetValue(angle);
    }

    private void OnZeroPresetChanged()
    {
        if (_loading) return;
        int index = _polarZeroPreset.SelectedIndex;
        if (index >= 0 && index < ZeroPresets.Length)
        {
            _loading = true;
            _polarZero.SetValue(ZeroPresets[index]);
            _loading = false;
        }
        OnSettingChanged();
    }

    /// <summary>Список лучей для чисел радиуса — углы лучей сетки, как они подписаны на шкале углов.</summary>
    private void FillRayList()
    {
        double step = _polarStep.Value;
        if (!(step >= 1) || step > 180) step = 30;
        string text = _radRay.Text;
        _radRay.BeginUpdate();
        _radRay.Items.Clear();
        for (double a = 0; a < 360 - 1e-9 && _radRay.Items.Count < 360; a += step)
        {
            _radRay.Items.Add(NumberFormatter.ForInput(a, 3));
        }
        _radRay.EndUpdate();
        _radRay.Text = text;
    }

    // ------------------------------------------------------------------ шкалы прямоугольной диаграммы

    private AxisSettings AxisAt(int index)
    {
        if (index < 0) return _project.XAxis;
        if (index == 0) return _project.YAxis;
        return index - 1 < _project.ExtraAxes.Count ? _project.ExtraAxes[index - 1] : null;
    }

    private void ShowAxisPage()
    {
        AxisSettings axis = AxisAt(_axisIndex);
        if (axis == null) return;
        bool extra = _axisIndex > 0;

        _loading = true;
        try
        {
            _axTitle.Text = axis.Title;
            _axUnit.Text = axis.Unit;
            _axSide.SelectedIndex = axis.Side == AxisSide.Left ? 0 : 1;
            _axLinear.Checked = !axis.Logarithmic;
            _axLog.Checked = axis.Logarithmic;
            _axAuto.Checked = axis.AutoLimits;
            _axMin.SetValue(axis.Min);
            _axMax.SetValue(axis.Max);
            _axAlign.Checked = axis.AlignToGrid;
            _axStep.SetValue(axis.GridStep);
            _axEvery.Value = Math.Max(1, Math.Min(100, axis.LabelEvery));
            _axDecimals.SelectedIndex = Math.Max(0, Math.Min(7, axis.Decimals + 1));
            _axPi.Checked = axis.Numbers == NumberStyle.PiFractions;

            _fAxSide.Visible = extra;
            _fAxAlign.Visible = extra;
            _fAxStep.Visible = !extra;
            _fAxScale.Text = axis.Logarithmic ? "Масштаб, мм на декаду" : "Масштаб, мм на единицу";
            if (_lastResult != null) ShowComputedAxis(_lastResult);
        }
        finally
        {
            _loading = false;
        }
        UpdateEnabledState();
    }

    private void OnAxisPageChanged()
    {
        if (_loading) return;
        AxisSettings axis = AxisAt(_axisIndex);
        if (axis == null) return;

        axis.Title = _axTitle.Text;
        axis.Unit = _axUnit.Text;
        if (_axisIndex > 0)
        {
            axis.Side = _axSide.SelectedIndex == 0 ? AxisSide.Left : AxisSide.Right;
            axis.AlignToGrid = _axAlign.Checked;
        }
        axis.Logarithmic = _axLog.Checked;
        axis.AutoLimits = _axAuto.Checked;
        if (!axis.AutoLimits)
        {
            axis.Min = _axMin.Value;
            axis.Max = _axMax.Value;
        }
        if (_axisIndex <= 0 && _project.Graph.GridMode != GridStepMode.Auto) axis.GridStep = _axStep.Value;
        axis.LabelEvery = (int)_axEvery.Value;
        axis.Decimals = _axDecimals.SelectedIndex - 1;
        axis.Numbers = _axPi.Checked ? NumberStyle.PiFractions : NumberStyle.Decimal;

        _fAxScale.Text = axis.Logarithmic ? "Масштаб, мм на декаду" : "Масштаб, мм на единицу";
        UpdateTreeTexts();
        FillCurveAxisList();
        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    private void AddExtra()
    {
        if (Kind != DiagramKind.Cartesian) return;
        _project.ExtraAxes.Add(new AxisSettings { Side = AxisSide.Right });
        int index = _project.ExtraAxes.Count;
        RebuildTree();
        SelectNode(new NodeTag(PageKind.Scale, index));
        FillCurveAxisList();
        MarkDirty();
        SchedulePreview();
        SetStatus("Добавлена шкала Y" + (index + 1).ToString(CultureInfo.CurrentCulture) +
                  ". Назначьте её кривой: «Кривые» → кривая → «Шкала».");
    }

    private void RemoveExtra()
    {
        if (_axisIndex <= 0 || AxisAt(_axisIndex) == null) return;

        // Кривые удаляемой шкалы переходят на основную; номера следующих шкал сдвигаются.
        int number = _axisIndex;
        foreach (CurveSettings curve in _project.Curves)
        {
            if (curve.Axis == number) curve.Axis = 0;
            else if (curve.Axis > number) curve.Axis--;
        }
        _project.ExtraAxes.RemoveAt(number - 1);
        _axisIndex = 0;
        RebuildTree();
        SelectNode(new NodeTag(PageKind.Scale, 0));
        FillCurveAxisList();
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Подпись единиц, в которых задаётся шаг сетки, — по выбранному способу.</summary>
    private void UpdateStepUnits()
    {
        string text;
        switch ((GridStepMode)Math.Max(0, _gridMode.SelectedIndex))
        {
            case GridStepMode.Units:
                text = "в единицах величины";
                break;
            case GridStepMode.Millimeters:
                text = "мм на листе";
                break;
            case GridStepMode.Divisions:
                text = "делений на шкале";
                break;
            default:
                text = "подбирается автоматически (см. «Диаграмма»)";
                break;
        }
        _axStepUnit.Text = _axLog.Checked ? "у логарифмической шкалы — по декадам" : text;
        _radStepUnit.Text = text;
    }

    // ------------------------------------------------------------------ габариты и масштаб

    /// <summary>Размах шкалы в тех единицах, в которых считается масштаб: у логарифмической — в декадах.</summary>
    private static double Range(double min, double max, bool log) =>
        log ? (min > 0 && max > min ? LogScale.Decades(min, max) : 1) : max - min;

    private void OnSizeEdited(SizeDriver driver, NumberBox source)
    {
        if (_loading || _lastResult == null) return;

        if (_project.Graph.Kind != DiagramKind.Cartesian)
        {
            // Полярная диаграмма задаётся диаметром, треугольная — длиной стороны.
            if (driver != SizeDriver.Width) return;
            _project.Graph.WidthMm = GraphSizeSolver.Clamp(source.Value);
            MarkDirty();
            SchedulePreview();
            return;
        }

        double rangeX = Range(_lastResult.XMin, _lastResult.XMax, _project.XAxis.Logarithmic);
        double rangeY = Range(_lastResult.YMin, _lastResult.YMax, _project.YAxis.Logarithmic);
        double width = _project.Graph.WidthMm, height = _project.Graph.HeightMm;
        GraphSizeSolver.Solve(driver, source.Value, rangeX, rangeY, _equalScale.Checked, ref width, ref height);

        _project.Graph.WidthMm = GraphSizeSolver.Clamp(width);
        _project.Graph.HeightMm = GraphSizeSolver.Clamp(height);
        _loading = true;
        if (source != _width) _width.SetValue(_project.Graph.WidthMm);
        if (source != _height) _height.SetValue(_project.Graph.HeightMm);
        _loading = false;

        MarkDirty();
        SchedulePreview();
    }

    /// <summary>При смене способа задания шага пересчитывает текущий шаг в новые единицы, чтобы сетка не «прыгала».</summary>
    private void OnGridModeChanged()
    {
        if (_loading) return;

        var mode = (GridStepMode)Math.Max(0, _gridMode.SelectedIndex);
        if (_lastResult?.TicksX != null && _lastResult.TicksY != null && mode != GridStepMode.Auto)
        {
            double Convert(double step, double range, double scale)
            {
                switch (mode)
                {
                    case GridStepMode.Millimeters: return Math.Round(step * scale, 6);
                    case GridStepMode.Divisions: return Math.Max(1, Math.Round(range / step));
                    default: return step;
                }
            }

            double stepX = _lastResult.TicksX.Step, stepY = _lastResult.TicksY.Step;
            if (stepX > 0 && _project.Graph.Kind == DiagramKind.Cartesian && !_project.XAxis.Logarithmic)
            {
                _project.XAxis.GridStep = Convert(stepX, _lastResult.XMax - _lastResult.XMin, _lastResult.ScaleX);
            }
            if (stepY > 0 && !_project.YAxis.Logarithmic)
            {
                _project.YAxis.GridStep = Convert(stepY, _lastResult.YMax - _lastResult.YMin, _lastResult.ScaleY);
            }
        }

        OnSettingChanged();
        _loading = true;
        AxisSettings axis = AxisAt(_axisIndex);
        if (axis != null) _axStep.SetValue(axis.GridStep);
        _radStep.SetValue(_project.YAxis.GridStep);
        _loading = false;
        UpdateStepUnits();
        UpdateEnabledState();
    }

    // ------------------------------------------------------------------ кривые

    private void SelectCurve(int index) => SelectNode(new NodeTag(PageKind.Curve, index));

    /// <summary>Показывает настройки выбранной кривой.</summary>
    private void ShowCurve()
    {
        if (_project.Curves.Count == 0) return;
        if (_selectedCurve >= _project.Curves.Count || _selectedCurve < 0) _selectedCurve = 0;

        CurveSettings curve = _project.Curves[_selectedCurve];
        _loading = true;
        try
        {
            _curveName.Text = curve.Name;
            _curveVisible.Checked = curve.Visible;
            _curveStyle.SelectedStyle = curve.LineStyle;
            _curveMode.SelectedIndex = curve.Mode == CurveMode.Smooth ? 0 : 1;
            _curveMarker.SelectedIndex = MarkerToIndex(curve.Marker);
            _curveMarkerSize.SetValue(curve.MarkerSizeMm);
            _curveMarkerPlacement.SelectedIndex = curve.MarkerPlacement == MarkerPlacement.Characteristic ? 1 : 0;
            _curveMarkerEvery.Value = Math.Max(1, Math.Min(100, curve.MarkerEvery));
            _curveLabelOn.Checked = curve.Label == CurveLabelMode.Leader;
            _curveLabelText.Text = curve.LabelText ?? "";
            _curveLabelAuto.Checked = curve.LabelPosition < 0;
            _curveLabelPos.SetValue(curve.LabelPosition < 0 ? 50 : Math.Min(100, curve.LabelPosition));
            _curveLabelDir.SelectedIndex = Math.Max(0, Math.Min(_curveLabelDir.Items.Count - 1, (int)curve.LabelDirection));
            FillCurveAxisList();
            ShowCurveColor(curve);
        }
        finally
        {
            _loading = false;
        }
        UpdateEnabledState();
    }

    /// <summary>Список шкал для выбранной кривой: основная и дополнительные.</summary>
    private void FillCurveAxisList()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _curveAxis.Items.Clear();
            for (int i = 0; i <= _project.ExtraAxes.Count; i++)
            {
                _curveAxis.Items.Add(AxisNodeText(i));
            }
            int axis = _selectedCurve >= 0 && _selectedCurve < _project.Curves.Count ? _project.Curves[_selectedCurve].Axis : 0;
            _curveAxis.SelectedIndex = axis > 0 && axis < _curveAxis.Items.Count ? axis : 0;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>Пользовательские цвета диалога выбора цвета — общие на время работы программы.</summary>
    private static int[] _customColors;

    private static Color ToColor(int rgb) =>
        Color.FromArgb(ColorValue.Red(rgb), ColorValue.Green(rgb), ColorValue.Blue(rgb));

    private void ShowCurveColor(CurveSettings curve)
    {
        int? rgb = ColorValue.Parse(curve.Color);
        Color? color = rgb.HasValue ? ToColor(rgb.Value) : (Color?)null;
        _curveColor.Value = color;
        _curveStyle.SampleColor = color;
    }

    private void ChooseCurveColor()
    {
        if (_selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) return;

        int? current = ColorValue.Parse(_project.Curves[_selectedCurve].Color);
        using (var dialog = new ColorDialog { FullOpen = true, AnyColor = true })
        {
            // Если цвет ещё не задан, диалог открывается на синем цвете основной линии КОМПАС.
            dialog.Color = current.HasValue ? ToColor(current.Value) : Color.FromArgb(0, 128, 255);
            if (_customColors != null) dialog.CustomColors = _customColors;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            _customColors = dialog.CustomColors;
            SetCurveColor(ColorValue.Format(ColorValue.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B)));
        }
    }

    /// <param name="color">Цвет «#RRGGBB» либо null — вернуть цвет стиля КОМПАС.</param>
    private void SetCurveColor(string color)
    {
        if (_selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) return;

        CurveSettings curve = _project.Curves[_selectedCurve];
        if (curve.Color == color) return;

        curve.Color = color;
        ShowCurveColor(curve);
        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Порядок строк списка маркеров совпадает с порядком значений <see cref="MarkerShape"/>.</summary>
    private static int MarkerToIndex(MarkerShape shape) => Math.Max(0, (int)shape);

    private MarkerShape SelectedMarker() =>
        _curveMarker.SelectedIndex <= 0 ? MarkerShape.None : (MarkerShape)_curveMarker.SelectedIndex;

    private void OnCurveSettingChanged()
    {
        if (_loading || _selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) return;

        CurveSettings curve = _project.Curves[_selectedCurve];
        bool renamed = curve.Name != _curveName.Text;
        curve.Name = _curveName.Text;
        curve.Visible = _curveVisible.Checked;
        curve.LineStyle = _curveStyle.SelectedStyle;
        curve.Mode = _curveMode.SelectedIndex == 1 ? CurveMode.Polyline : CurveMode.Smooth;
        curve.Marker = SelectedMarker();
        curve.MarkerSizeMm = _curveMarkerSize.Value;
        curve.MarkerPlacement = _curveMarkerPlacement.SelectedIndex == 1 ? MarkerPlacement.Characteristic : MarkerPlacement.DataPoints;
        curve.MarkerEvery = (int)_curveMarkerEvery.Value;
        curve.Axis = Math.Max(0, _curveAxis.SelectedIndex);
        curve.Label = _curveLabelOn.Checked ? CurveLabelMode.Leader : CurveLabelMode.None;
        curve.LabelText = _curveLabelText.Text;
        curve.LabelPosition = _curveLabelAuto.Checked ? -1 : _curveLabelPos.Value;
        curve.LabelDirection = (LabelDirection)Math.Max(0, _curveLabelDir.SelectedIndex);

        if (renamed)
        {
            UpdateTreeTexts();
            _grid.RefreshHeaders();
        }

        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    private void AddCurve()
    {
        CommitEdits();
        int index = _grid.AddCurve();
        RebuildTree();
        SelectCurve(index);
        MarkDirty();
        SchedulePreview();
        SetStatus("Добавлен столбец «" + _project.Curves[index].Name + "». Введите или вставьте значения Y.");
    }

    private void RemoveCurve()
    {
        if (_project.Curves.Count <= 1) return;
        if (_selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) _selectedCurve = 0;

        string name = _project.Curves[_selectedCurve].Name;
        DialogResult answer = MessageBox.Show(this, "Удалить кривую «" + name + "» вместе со столбцом данных?", AppTitle,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        _grid.RemoveCurve(_selectedCurve);
        _selectedCurve = Math.Min(_selectedCurve, _project.Curves.Count - 1);
        RebuildTree();
        SelectCurve(_selectedCurve);
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Набор кривых изменился после вставки или импорта.</summary>
    private void OnCurvesChanged()
    {
        UseHeadersAsTitles();
        RebuildTree();
    }

    /// <summary>
    /// Если подписи осей ещё не заданы, а таблица пришла с заголовками, берём заголовки:
    /// столбцы «x, мм» и «F, Н» из Mathcad или Excel сразу становятся подписями осей.
    /// </summary>
    private void UseHeadersAsTitles()
    {
        bool changed = false;
        string xName = _project.Data.XName;
        if (string.IsNullOrWhiteSpace(_project.XAxis.Title) && !string.IsNullOrWhiteSpace(xName) && xName != "X")
        {
            _project.XAxis.Title = xName;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(_project.YAxis.Title) && _project.Curves.Count == 1)
        {
            string yName = _project.Curves[0].Name;
            if (!string.IsNullOrWhiteSpace(yName) && yName != "Y" && yName != "Y1")
            {
                _project.YAxis.Title = yName;
                changed = true;
            }
        }
        if (!changed) return;

        _loading = true;
        _radTitle.Text = _project.YAxis.Title;
        _ternA.Text = _project.XAxis.Title;
        _ternB.Text = _project.YAxis.Title;
        _loading = false;
        if (_currentPage == PageKind.Scale) ShowAxisPage();
    }

    // ------------------------------------------------------------------ вычисленные значения

    /// <summary>Показывает вычисленные величины: пределы в режиме «авто», масштабы, шаг сетки.</summary>
    private void ShowComputedValues(BuildResult result)
    {
        bool cartesian = result.Kind == DiagramKind.Cartesian;
        _loading = true;
        try
        {
            if (!cartesian)
            {
                _height.SetValue(result.HeightMm);
            }
            else if (_project.Graph.EqualScale && Math.Abs(_project.Graph.HeightMm - result.HeightMm) > 1e-9)
            {
                // При одинаковом масштабе высота следует за шириной.
                _project.Graph.HeightMm = result.HeightMm;
                _height.SetValue(result.HeightMm);
            }

            if (cartesian) ShowComputedAxis(result);

            if (result.Kind == DiagramKind.Polar)
            {
                if (_radAuto.Checked)
                {
                    _radMin.SetValue(result.YMin);
                    _radMax.SetValue(result.YMax);
                }
                if (_gridMode.SelectedIndex <= 0 && result.TicksY != null) _radStep.SetValue(result.TicksY.Step);
            }
        }
        finally
        {
            _loading = false;
        }

        string info;
        switch (result.Kind)
        {
            case DiagramKind.Polar:
                info = string.Format(CultureInfo.CurrentCulture, "Радиус: {0} … {1};  диаметр {2} мм;  точек: {3}",
                    NumberFormatter.ForInput(result.YMin), NumberFormatter.ForInput(result.YMax),
                    NumberFormatter.ForInput(result.WidthMm, 2), result.SourcePointCount);
                break;

            case DiagramKind.Ternary:
                info = string.Format(CultureInfo.CurrentCulture, "Сумма составляющих: {0};  сторона {1} мм;  точек: {2}",
                    NumberFormatter.ForInput(result.XMax), NumberFormatter.ForInput(result.WidthMm, 2), result.SourcePointCount);
                break;

            default:
                info = string.Format(CultureInfo.CurrentCulture, "X: {0} … {1};  Y: {2} … {3}",
                    NumberFormatter.ForInput(result.XMin), NumberFormatter.ForInput(result.XMax),
                    NumberFormatter.ForInput(result.YMin), NumberFormatter.ForInput(result.YMax));
                for (int i = 1; i < result.YScales.Count; i++)
                {
                    info += string.Format(CultureInfo.CurrentCulture, ";  {0}: {1} … {2}", result.YScales[i].Name,
                        NumberFormatter.ForInput(result.YScales[i].Min), NumberFormatter.ForInput(result.YScales[i].Max));
                }
                info += string.Format(CultureInfo.CurrentCulture, ";  поле {0}×{1} мм;  точек: {2}",
                    NumberFormatter.ForInput(result.WidthMm, 2), NumberFormatter.ForInput(result.HeightMm, 2), result.SourcePointCount);
                break;
        }
        if (result.Decimated)
        {
            info += string.Format(CultureInfo.CurrentCulture, ", после прореживания вершин: {0}", result.CurveVertexCount);
        }
        _infoLabel.Text = info;
    }

    /// <summary>Вычисленные пределы, масштаб и шаг выбранной шкалы прямоугольной диаграммы.</summary>
    private void ShowComputedAxis(BuildResult result)
    {
        if (result.Kind != DiagramKind.Cartesian) return;
        AxisSettings axis = AxisAt(_axisIndex);
        if (axis == null) return;

        double min, max, scale, step;
        if (_axisIndex < 0)
        {
            min = result.XMin;
            max = result.XMax;
            scale = result.ScaleX;
            step = result.TicksX?.Step ?? 0;
        }
        else if (_axisIndex < result.YScales.Count)
        {
            ScaleResult s = result.YScales[_axisIndex];
            min = s.Min;
            max = s.Max;
            scale = s.MmPerUnit;
            step = s.Step;
        }
        else
        {
            return;
        }

        if (axis.AutoLimits)
        {
            _axMin.SetValue(min);
            _axMax.SetValue(max);
        }
        _axScale.SetValue(scale);
        if (_gridMode.SelectedIndex <= 0 && step > 0) _axStep.SetValue(step);
    }

    private sealed class IssueItem
    {
        public DataIssue Issue;
        public override string ToString() =>
            (Issue.Severity == IssueSeverity.Error ? "Ошибка: " : "Внимание: ") + Issue.Message;
    }

    private void ShowIssues(BuildResult result)
    {
        _issuesList.BeginUpdate();
        _issuesList.Items.Clear();
        foreach (DataIssue issue in result.Issues)
        {
            // Пустая таблица — не ошибка для показа: об этом говорит надпись в предпросмотре.
            if (result.SourcePointCount == 0 && issue.Row == 0 && issue.Column < 0 && issue.Message.Contains("пуста")) continue;
            _issuesList.Items.Add(new IssueItem { Issue = issue });
        }
        _issuesList.EndUpdate();
        _issuesList.Visible = _issuesList.Items.Count > 0;
    }
}
