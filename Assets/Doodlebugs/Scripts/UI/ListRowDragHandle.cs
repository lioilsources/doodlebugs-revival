using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drag grip for one row of a vertical list (the hangar's arena rotation).
///
/// Dragging the grip lifts its row out of the layout onto a drag layer,
/// leaves a placeholder gap that follows the pointer up and down the list,
/// and on release reports where the row landed. The grip is a dedicated
/// zone rather than the whole row because the list lives in a ScrollRect:
/// a drag anywhere else must keep scrolling, and a tap on the row must keep
/// toggling it - so the grip also swallows clicks.
/// </summary>
public class ListRowDragHandle : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    /// <summary>The row this grip moves - a direct child of Content.</summary>
    public RectTransform Row;

    /// <summary>The layout parent holding every row.</summary>
    public RectTransform Content;

    /// <summary>Where the row floats while dragged. Must sit outside the
    /// scroll mask so the row stays visible past the viewport edges.</summary>
    public RectTransform DragLayer;

    /// <summary>Nudged when the pointer nears the viewport's top or bottom
    /// edge, so a row can be dragged to a slot that is scrolled out of view.</summary>
    public ScrollRect Scroll;

    /// <summary>(fromIndex, toIndex) as sibling indices in Content, on release.</summary>
    public Action<int, int> OnReordered;

    private const float EdgeZone = 60f;    // px from the viewport edge where auto-scroll kicks in
    private const float EdgeSpeed = 700f;  // px per second

    private RectTransform _placeholder;
    private int _fromIndex = -1;
    private float _grabOffsetY;
    private float _lockedX;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Row == null || Content == null || _placeholder != null) return;

        _fromIndex = Row.GetSiblingIndex();

        // The gap that keeps the list's shape while the row is away.
        var gap = new GameObject("DragPlaceholder", typeof(RectTransform), typeof(LayoutElement));
        _placeholder = gap.GetComponent<RectTransform>();
        _placeholder.SetParent(Content, false);
        _placeholder.SetSiblingIndex(_fromIndex);
        var gapLayout = gap.GetComponent<LayoutElement>();
        gapLayout.preferredHeight = Row.rect.height;
        gapLayout.minHeight = Row.rect.height;

        // Lift the row onto the drag layer without it moving on screen.
        var layer = DragLayer != null ? DragLayer : Content;
        Row.SetParent(layer, true);
        Row.SetAsLastSibling();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, eventData.position,
                eventData.pressEventCamera, out var pointerLocal))
        {
            _grabOffsetY = Row.localPosition.y - pointerLocal.y;
        }
        _lockedX = Row.localPosition.x;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_placeholder == null) return;

        var layer = (RectTransform)Row.parent;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, eventData.position,
                eventData.pressEventCamera, out var pointerLocal))
        {
            Row.localPosition = new Vector3(_lockedX, pointerLocal.y + _grabOffsetY, 0f);
        }

        NudgeScrollAtEdges(eventData);

        // The gap goes after every row whose centre is still above ours.
        float rowY = Row.TransformPoint(Row.rect.center).y;
        int target = 0;
        for (int i = 0; i < Content.childCount; i++)
        {
            var child = (RectTransform)Content.GetChild(i);
            if (child == _placeholder) continue;
            if (child.TransformPoint(child.rect.center).y > rowY) target++;
        }
        if (_placeholder.GetSiblingIndex() != target) _placeholder.SetSiblingIndex(target);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_placeholder == null) return;

        int to = _placeholder.GetSiblingIndex();
        // Destroy() is deferred; detach first so the index below is exact.
        _placeholder.SetParent(null, false);
        Destroy(_placeholder.gameObject);
        _placeholder = null;

        Row.SetParent(Content, false);
        Row.SetSiblingIndex(to);

        int from = _fromIndex;
        _fromIndex = -1;
        OnReordered?.Invoke(from, to);
    }

    /// <summary>A tap on the grip must not toggle the row underneath.</summary>
    public void OnPointerClick(PointerEventData eventData) { }

    private void NudgeScrollAtEdges(PointerEventData eventData)
    {
        if (Scroll == null || Scroll.viewport == null || Scroll.content == null) return;

        var viewport = Scroll.viewport;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position,
                eventData.pressEventCamera, out var inViewport)) return;

        float step = EdgeSpeed * Time.unscaledDeltaTime;
        float dy = 0f;
        if (inViewport.y > viewport.rect.yMax - EdgeZone) dy = -step;      // reveal rows above
        else if (inViewport.y < viewport.rect.yMin + EdgeZone) dy = step;  // reveal rows below
        if (dy == 0f) return;

        // Content is top-anchored: y = 0 shows the top, larger y scrolls down.
        var content = Scroll.content;
        float overflow = Mathf.Max(0f, content.rect.height - viewport.rect.height);
        var p = content.anchoredPosition;
        p.y = Mathf.Clamp(p.y + dy, 0f, overflow);
        content.anchoredPosition = p;
    }

    private void OnDestroy()
    {
        if (_placeholder != null) Destroy(_placeholder.gameObject);
    }
}
