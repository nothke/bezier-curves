using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(BezierCurve))]
public class BezierCurveEditor : Editor
{
    BezierCurve curve;
    SerializedProperty resolutionProp;
    SerializedProperty closeProp;
    SerializedProperty pointsProp;
    SerializedProperty colorProp;

    private static bool showPoints = true;

    EditorApplication.CallbackFunction dlg;
    BezierPoint pointToDestroy;

    bool createDragging;

    enum ToolMode { None, Creating, Editing };
    ToolMode toolMode;
    ToolMode lastToolMode = ToolMode.None;

    string[] toolModesText = { "None", "Add", "Multiedit" };

    Vector2 selectionStartPos;

    bool regionSelect;

    List<int> selectedPoints;

    Quaternion multieditRotation = Quaternion.identity;
    Quaternion lastRotation = Quaternion.identity;


    void OnEnable()
    {
        curve = (BezierCurve)target;

        resolutionProp = serializedObject.FindProperty("resolution");
        closeProp = serializedObject.FindProperty("_close");
        pointsProp = serializedObject.FindProperty("points");
        colorProp = serializedObject.FindProperty("drawColor");

        dlg = new EditorApplication.CallbackFunction(RemovePoint);

        selectedPoints = new List<int>();

        if (toolMode == ToolMode.Editing)
            Tools.hidden = true;
        else
            ExitEditMode();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(resolutionProp);
        EditorGUILayout.PropertyField(closeProp);
        EditorGUILayout.PropertyField(colorProp);

        showPoints = EditorGUILayout.Foldout(showPoints, "Points");

        if (showPoints)
        {
            int pointCount = pointsProp.arraySize;

            for (int i = 0; i < pointCount; i++)
            {
                DrawPointInspector(curve[i], i);
            }

            /*
            if (GUILayout.Button("Add Point"))
            {

                GameObject pointObject = new GameObject("Point " + pointsProp.arraySize);
                pointObject.transform.parent = curve.transform;

                Undo.RegisterCreatedObjectUndo(pointObject, "Add Point");
                Undo.RecordObject(curve, "Add Point");

                // Nothke: place at last point position instead
                if (pointCount != 0) pointObject.transform.localPosition = curve.GetAnchorPoints()[pointCount - 1].localPosition + Vector3.forward * 50;
                else pointObject.transform.localPosition = Vector3.zero;

                BezierPoint newPoint = pointObject.AddComponent<BezierPoint>();

                newPoint.curve = curve;
                newPoint.handle1 = Vector3.right * 20;
                newPoint.handle2 = -Vector3.right * 20;

                pointsProp.InsertArrayElementAtIndex(pointsProp.arraySize);
                pointsProp.GetArrayElementAtIndex(pointsProp.arraySize - 1).objectReferenceValue = newPoint;
            }*/
        }

        toolMode = (ToolMode)GUILayout.SelectionGrid((int)toolMode, toolModesText, 3);

        if (toolMode != lastToolMode)
        {
            if (toolMode == ToolMode.Editing)
                Tools.hidden = true;
            else
                ExitEditMode();
        }

        lastToolMode = toolMode;

        if (GUILayout.Button("Center pivot"))
        {
            curve.CenterPivot();
            serializedObject.ApplyModifiedProperties();
            Undo.RegisterFullObjectHierarchyUndo(curve, "Center pivot");
        }

        if (GUI.changed)
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }

    private void OnDisable()
    {
        ExitEditMode();
    }

    void ExitEditMode()
    {
        selectedPoints.Clear();

        Tools.hidden = false;
    }

    void OnSceneGUI()
    {
        for (int i = 0; i < curve.pointCount; i++)
        {
            DrawPointSceneGUI(curve[i]);
        }

        if (toolMode == ToolMode.Creating)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            Vector3 targetPoint;
            Vector3 targetNormal;
            if (GetMouseSceneHit(out RaycastHit hit))
            {
                targetPoint = hit.point;
                targetNormal = hit.normal;
            }
            else
            {
                Vector2 guiPosition = Event.current.mousePosition;
                Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);

                if (curve.pointCount > 0)
                {
                    Plane plane = new Plane(Vector3.up, curve.Last().position);

                    plane.Raycast(ray, out float d);
                    targetPoint = ray.GetPoint(d);
                }
                else
                {
                    Plane plane = new Plane(Vector3.up, curve.transform.position);

                    plane.Raycast(ray, out float d);
                    targetPoint = ray.GetPoint(d);
                }

                targetNormal = Vector3.up;
            }

            Handles.ArrowHandleCap(0,
                targetPoint, Quaternion.LookRotation(targetNormal, Vector3.forward),
                20, EventType.Repaint);

            SceneView.RepaintAll();

            if (createDragging)
            {
                curve[curve.pointCount - 1].globalHandle2 = targetPoint;
            }

            if (Event.current.type == EventType.MouseDown)
            {
                if (Event.current.button == 0)
                {
                    GUIUtility.hotControl = controlId;
                    Event.current.Use();

                    createDragging = true;

                    curve.AddPointAt(targetPoint);
                }
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                if (Event.current.button == 0)
                {
                    createDragging = false;
                }
            }
        }
        else if (toolMode == ToolMode.Editing)
        {
            //SceneView.lastActiveSceneView.drawGizmos = false;



            int controlId = GUIUtility.GetControlID(FocusType.Passive);



            if (regionSelect)
            {
                Transform camT = SceneView.lastActiveSceneView.camera.transform;

                var mousePos = Event.current.mousePosition;
                Rect r = new Rect(selectionStartPos, mousePos - selectionStartPos);

                selectedPoints.Clear();
                for (int i = 0; i < curve.pointCount; i++)
                {
                    var point = HandleUtility.WorldToGUIPoint(curve[i].position);
                    if (r.Contains(point))
                    {
                        selectedPoints.Add(i);
                    }
                }

                //HandleUtility.WorldToGUIPoint()

                Handles.BeginGUI();
                GUI.Box(r, new GUIContent());
                Handles.EndGUI();

                SceneView.RepaintAll();
            }

            Vector3 avgPosition = Vector3.zero;

            int sct = selectedPoints.Count;
            for (int sp = 0; sp < sct; sp++)
            {
                int i = selectedPoints[sp];

                Vector3 pos = curve[i].position;
                avgPosition += pos / sct;

                float size = HandleUtility.GetHandleSize(pos) * 0.1f;
                Handles.SphereHandleCap(-1, pos, Quaternion.identity, size, EventType.Repaint);
            }

            if (selectedPoints.Count > 0)
            {
                if (Tools.current == Tool.Move)
                {
                    Vector3 targetPos = Handles.PositionHandle(avgPosition, Quaternion.identity);

                    Vector3 diff = avgPosition - targetPos;

                    if (diff != Vector3.zero)
                    {
                        for (int sp = 0; sp < sct; sp++)
                        {
                            int i = selectedPoints[sp];
                            curve[i].position -= diff;
                        }
                    }
                }
                else if (Tools.current == Tool.Rotate)
                {
                    if (Event.current.button == 0 && Event.current.type == EventType.MouseUp)
                    {
                        multieditRotation = Quaternion.identity;
                        lastRotation = Quaternion.identity;
                    }

                    multieditRotation = Handles.RotationHandle(multieditRotation, avgPosition);


                    Quaternion rotDiff = multieditRotation * Quaternion.Inverse(lastRotation);

                    lastRotation = multieditRotation;

                    if (rotDiff != Quaternion.identity)
                    {
                        //Debug.Log(rotDiff);
                        for (int sp = 0; sp < sct; sp++)
                        {
                            int i = selectedPoints[sp];

                            Vector3 posDiff = curve[i].position - avgPosition;
                            Vector3 newPos = rotDiff * posDiff;

                            curve[i].position = avgPosition + newPos;
                            curve[i].handle1 = rotDiff * curve[i].handle1;
                            //curve[i].transform.rotation *= targetRot;
                        }
                    }
                }
            }

            if (Event.current.button == 0)
            {
                if (Event.current.type == EventType.MouseDown)
                {
                    GUIUtility.hotControl = controlId;
                    Event.current.Use();

                    selectionStartPos = Event.current.mousePosition;

                    regionSelect = true;
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    //GUIUtility.hotControl = controlId;
                    //Event.current.Use();

                    multieditRotation = Quaternion.identity;
                    lastRotation = Quaternion.identity;

                    regionSelect = false;
                }
            }
        }
    }

    public void RemovePoint()
    {
        Undo.DestroyObjectImmediate(pointToDestroy.gameObject);
        EditorApplication.delayCall -= dlg;
    }

    void DrawPointInspector(BezierPoint point, int index)
    {
        SerializedObject serObj = new SerializedObject(point);

        SerializedProperty handleStyleProp = serObj.FindProperty("handleStyle");
        SerializedProperty handle1Prop = serObj.FindProperty("_handle1");
        SerializedProperty handle2Prop = serObj.FindProperty("_handle2");

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("X", GUILayout.Width(20)))
        {
            //Undo.RegisterSceneUndo("Remove Point");
            pointsProp.MoveArrayElement(curve.GetPointIndex(point), curve.pointCount - 1);
            pointsProp.arraySize--;
            Undo.RecordObject(curve, "Remove Point");

            EditorApplication.delayCall += dlg;
            pointToDestroy = point;
            //Undo.DestroyObjectImmediate(point.gameObject);

            //Undo.RegisterCompleteObjectUndo(curve, "Remove Point");
            //DestroyImmediate(point.gameObject);
            return;
        }

        EditorGUILayout.ObjectField(point.gameObject, typeof(GameObject), true);

        if (index != 0 && GUILayout.Button(@"/\", GUILayout.Width(25)))
        {
            UnityEngine.Object other = pointsProp.GetArrayElementAtIndex(index - 1).objectReferenceValue;
            pointsProp.GetArrayElementAtIndex(index - 1).objectReferenceValue = point;
            pointsProp.GetArrayElementAtIndex(index).objectReferenceValue = other;
        }

        if (index != pointsProp.arraySize - 1 && GUILayout.Button(@"\/", GUILayout.Width(25)))
        {
            UnityEngine.Object other = pointsProp.GetArrayElementAtIndex(index + 1).objectReferenceValue;
            pointsProp.GetArrayElementAtIndex(index + 1).objectReferenceValue = point;
            pointsProp.GetArrayElementAtIndex(index).objectReferenceValue = other;
        }

        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel++;
        EditorGUI.indentLevel++;

        int newType = (int)((object)EditorGUILayout.EnumPopup("Handle Type", (BezierPoint.HandleStyle)handleStyleProp.enumValueIndex));

        if (newType != handleStyleProp.enumValueIndex)
        {
            handleStyleProp.enumValueIndex = newType;
            if (newType == 0)
            {
                if (handle1Prop.vector3Value != Vector3.zero) handle2Prop.vector3Value = -handle1Prop.vector3Value;
                else if (handle2Prop.vector3Value != Vector3.zero) handle1Prop.vector3Value = -handle2Prop.vector3Value;
                else
                {
                    handle1Prop.vector3Value = new Vector3(0.1f, 0, 0);
                    handle2Prop.vector3Value = new Vector3(-0.1f, 0, 0);
                }
            }

            else if (newType == 1)
            {
                if (handle1Prop.vector3Value == Vector3.zero && handle2Prop.vector3Value == Vector3.zero)
                {
                    handle1Prop.vector3Value = new Vector3(0.1f, 0, 0);
                    handle2Prop.vector3Value = new Vector3(-0.1f, 0, 0);
                }
            }

            else if (newType == 2)
            {
                handle1Prop.vector3Value = Vector3.zero;
                handle2Prop.vector3Value = Vector3.zero;
            }
        }

        Vector3 newPointPos = EditorGUILayout.Vector3Field("Position : ", point.transform.localPosition);
        if (newPointPos != point.transform.localPosition)
        {
            Undo.RegisterCompleteObjectUndo(point.transform, "Move Bezier Point");
            point.transform.localPosition = newPointPos;
        }

        if (handleStyleProp.enumValueIndex == 0)
        {
            Vector3 newPosition;

            newPosition = EditorGUILayout.Vector3Field("Handle 1", handle1Prop.vector3Value);
            if (newPosition != handle1Prop.vector3Value)
            {
                handle1Prop.vector3Value = newPosition;
                handle2Prop.vector3Value = -newPosition;
            }

            newPosition = EditorGUILayout.Vector3Field("Handle 2", handle2Prop.vector3Value);
            if (newPosition != handle2Prop.vector3Value)
            {
                handle1Prop.vector3Value = -newPosition;
                handle2Prop.vector3Value = newPosition;
            }
        }

        else if (handleStyleProp.enumValueIndex == 1)
        {
            EditorGUILayout.PropertyField(handle1Prop);
            EditorGUILayout.PropertyField(handle2Prop);
        }

        EditorGUI.indentLevel--;
        EditorGUI.indentLevel--;

        if (GUI.changed)
        {
            serObj.ApplyModifiedProperties();
            EditorUtility.SetDirty(serObj.targetObject);
        }
    }

    static void DrawPointSceneGUI(BezierPoint point)
    {
        Handles.Label(point.position + new Vector3(0, HandleUtility.GetHandleSize(point.position) * 0.4f, 0), point.gameObject.name);

        Handles.color = Color.green;
        Vector3 newPosition = Handles.FreeMoveHandle(point.position, point.transform.rotation, HandleUtility.GetHandleSize(point.position) * 0.1f, Vector3.zero, Handles.RectangleHandleCap);

        if (newPosition != point.position)
        {
            Undo.RegisterCompleteObjectUndo(point.transform, "Move Point");
            point.transform.position = newPosition;
        }

        if (point.handleStyle != BezierPoint.HandleStyle.None)
        {
            Handles.color = Color.cyan;
            Vector3 newGlobal1 = Handles.FreeMoveHandle(point.globalHandle1, point.transform.rotation, HandleUtility.GetHandleSize(point.globalHandle1) * 0.075f, Vector3.zero, Handles.CircleHandleCap);
            if (point.globalHandle1 != newGlobal1)
            {
                Undo.RegisterCompleteObjectUndo(point, "Move Handle");
                point.globalHandle1 = newGlobal1;
                if (point.handleStyle == BezierPoint.HandleStyle.Connected) point.globalHandle2 = -(newGlobal1 - point.position) + point.position;
            }

            Vector3 newGlobal2 = Handles.FreeMoveHandle(point.globalHandle2, point.transform.rotation, HandleUtility.GetHandleSize(point.globalHandle2) * 0.075f, Vector3.zero, Handles.CircleHandleCap);
            if (point.globalHandle2 != newGlobal2)
            {
                Undo.RegisterCompleteObjectUndo(point, "Move Handle");
                point.globalHandle2 = newGlobal2;
                if (point.handleStyle == BezierPoint.HandleStyle.Connected) point.globalHandle1 = -(newGlobal2 - point.position) + point.position;
            }

            Handles.color = Color.yellow;
            Handles.DrawLine(point.position, point.globalHandle1);
            Handles.DrawLine(point.position, point.globalHandle2);
        }
    }

    public static void DrawOtherPoints(BezierCurve curve, BezierPoint caller)
    {
        foreach (BezierPoint p in curve.GetAnchorPoints())
        {
            if (p != caller) DrawPointSceneGUI(p);
        }
    }

    [MenuItem("GameObject/Create Other/Bezier Curve")]
    public static void CreateCurve(MenuCommand command)
    {
        GameObject curveObject = new GameObject("BezierCurve");
        Undo.RegisterCreatedObjectUndo(curveObject, "Undo Create Curve");
        BezierCurve curve = curveObject.AddComponent<BezierCurve>();

        BezierPoint p1 = curve.AddPointAt(Vector3.forward * 0.5f);
        p1.handleStyle = BezierPoint.HandleStyle.Connected;
        p1.handle1 = new Vector3(-0.28f, 0, 0);

        BezierPoint p2 = curve.AddPointAt(Vector3.right * 0.5f);
        p2.handleStyle = BezierPoint.HandleStyle.Connected;
        p2.handle1 = new Vector3(0, 0, 0.28f);

        BezierPoint p3 = curve.AddPointAt(-Vector3.forward * 0.5f);
        p3.handleStyle = BezierPoint.HandleStyle.Connected;
        p3.handle1 = new Vector3(0.28f, 0, 0);

        BezierPoint p4 = curve.AddPointAt(-Vector3.right * 0.5f);
        p4.handleStyle = BezierPoint.HandleStyle.Connected;
        p4.handle1 = new Vector3(0, 0, -0.28f);

        curve.close = true;
    }

    Vector3 GetMouseScenePos()
    {
        Vector2 guiPosition = Event.current.mousePosition;
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);

        RaycastHit hit;
        Physics.Raycast(ray, out hit);

        return hit.point;
    }

    bool GetMouseSceneHit(out RaycastHit hit)
    {
        Vector2 guiPosition = Event.current.mousePosition;
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);

        return Physics.Raycast(ray, out hit);
    }
}
