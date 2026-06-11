namespace UnityEngine
{
    [UnityEngine.Scripting.Preserve]
    public static class RectTransformExtensions
    {
        //重置为全屏自适应UI
        public static void ResetToFullScreen(this RectTransform self)
        {
            self.anchorMin = Vector2.zero;
            self.anchorMax = Vector2.one;
            self.anchoredPosition3D = Vector3.zero;
            self.pivot = new Vector2(0.5f, 0.5f);
            self.offsetMax = Vector2.zero;
            self.offsetMin = Vector2.zero;
            self.sizeDelta = Vector2.zero;
            self.localEulerAngles = Vector3.zero;
            self.localScale = Vector3.one;
        }

        //重置位置与旋转
        public static void ResetLocalPosAndRot(this RectTransform self)
        {
            self.localPosition = Vector3.zero;
            self.localRotation = Quaternion.identity;
        }

        public static void SetWidth(this RectTransform rectTransform, float width)
        {
            Vector2 size = rectTransform.sizeDelta;
            size.x = width;
            rectTransform.sizeDelta = size;
        }

        public static void SetHeight(this RectTransform rectTransform, float height)
        {
            Vector2 size = rectTransform.sizeDelta;
            size.y = height;
            rectTransform.sizeDelta = size;
        }

        public static void SetAnchoredX(this RectTransform rectTransform, float x)
        {
            Vector2 position = rectTransform.anchoredPosition;
            position.x = x;
            rectTransform.anchoredPosition = position;
        }

        public static void SetAnchoredY(this RectTransform rectTransform, float y)
        {
            Vector2 position = rectTransform.anchoredPosition;
            position.y = y;
            rectTransform.anchoredPosition = position;
        }

        public static System.Collections.Generic.IEnumerable<RectTransform> GetChildTransforms(this RectTransform rectTransform)
        {
            foreach (var item in rectTransform)
            {
                yield return item as RectTransform;
            }
        }
    }
}
