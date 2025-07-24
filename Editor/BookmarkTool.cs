#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using System.IO.Compression;

// Object型の曖昧さ回避
using Object = UnityEngine.Object;



/// <summary>
/// ブックマークツール <para/>
/// このツールは、Unityエディター内でアセットのブックマークを管理するためのものです。
/// </summary>
public class BookmarkTool : EditorWindow
{
	[System.Serializable]
	public class BookmarkEntry
	{
		public string name;
		public Object asset;
		public string group;
		public string assetPath;
	}

	private List<BookmarkEntry> bookmarks = new List<BookmarkEntry>();
	private Vector2 scrollPos;


	private string newGroup = "";

	private List<string> customGroups = new List<string>();
	private string newGroupName = "";

	private Dictionary<string, bool> groupFoldouts = new Dictionary<string, bool>();

	private int selectedTab = 0;
	private readonly string[] tabNames = { "ブックマーク編集", "グループ編集" };

	private int dragSourceIndex = -1;
	private int dragTargetIndex = -1;

	private int groupDragSourceIndex = -1;
	private int groupDragTargetIndex = -1;

	// 直近の保存データを保持
	private List<BookmarkEntry> savedBookmarks = new List<BookmarkEntry>();
	private List<string> savedCustomGroups = new List<string>();

	// 表示モード用のenumと変数を追加
	private enum BookmarkDisplayMode { Grouped, Flat }
	private BookmarkDisplayMode displayMode = BookmarkDisplayMode.Flat;

	// クラス内（他のフィールドと同じ場所に追加）
	private int renameGroupIndex = -1;
	private string renameGroupName = "";

	// クラスフィールドに追加
	private bool showPath = false;

	[MenuItem("Tools/Bookmark Tool")]
	public static void ShowWindow()
	{
		GetWindow<BookmarkTool>("Bookmark Tool");
	}

	private void OnGUI()
	{
		// 差分があればタイトルに＊を付与
		string title = "Bookmark Tool";
		if (HasDifference()) title += "＊";
		titleContent.text = title;

		selectedTab = GUILayout.Toolbar(selectedTab, tabNames);

		EditorGUILayout.Space();

		switch (selectedTab)
		{
			case 0: // ブックマーク編集
				DrawBookmarkView();
				break;
			case 1: // グループ編集
				DrawGroupCreate();
				break;
		}


		// 下部にスペースを入れてボタンを下端に配置
		GUILayout.FlexibleSpace();
		EditorGUILayout.LabelField("ブックマークデータ", EditorStyles.boldLabel);

		// --- 保存先表示チェックボックスを右端に配置 ---
		EditorGUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		showPath = EditorGUILayout.ToggleLeft("保存先を表示", showPath, GUILayout.Width(90));
		EditorGUILayout.EndHorizontal();

		if (showPath)
		{
			// 小さいテキストスタイル
			GUIStyle smallLabel = new GUIStyle(EditorStyles.label);
			smallLabel.fontSize = 10;

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.SelectableLabel(GetBookmarkFilePath(), smallLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
			EditorGUILayout.EndHorizontal();
		}

		// --- 横並びで表示 ---
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("保存", GUILayout.Height(25)))
		{
			SaveBookmarks();
		}
		if (GUILayout.Button("読込", GUILayout.Height(25)))
		{
			LoadBookmarks();
		}
		EditorGUILayout.EndHorizontal();
	}

	private static readonly Color DragHighlightColor = new Color(0.8f, 0.9f, 1.0f, 1.0f); // Light blue
	private static readonly Color DragOverColor = new Color(0.7f, 0.85f, 1.0f, 1.0f); // Slightly deeper blue
	private static readonly Color HoverTextColor = Color.yellow; // マウスオーバー時のテキスト色
	private static readonly Color NormalTextColor = Color.white; // 通常時のテキスト色

	// ドラッグエリアの表示状態を管理する辞書
	private Dictionary<string, bool> groupDragAreaVisible = new Dictionary<string, bool>();

	// --- 絞り込み用変数 ---
	private string filterGroup = "すべて";
	private System.Type filterType = null;

	// --- アセット型リスト ---
	private readonly Dictionary<string, System.Type> assetTypeOptions = new Dictionary<string, System.Type>
	{
		{ "すべて", null },
		{ "AnimationClip", typeof(AnimationClip) },
		{ "AnimatorController", typeof(UnityEditor.Animations.AnimatorController) },
		{ "AudioClip", typeof(AudioClip) },
		{ "Avatar", typeof(UnityEngine.Avatar) },
		{ "Cubemap", typeof(Cubemap) },
		{ "DefaultAsset", typeof(UnityEditor.DefaultAsset) },
		{ "Font", typeof(Font) },
		{ "Material", typeof(Material) },
		{ "Mesh", typeof(Mesh) },
		{ "MeshFilter", typeof(MeshFilter) },
		{ "MeshRenderer", typeof(MeshRenderer) },
		{ "NavMeshData", typeof(UnityEngine.AI.NavMeshData) },
		{ "PhysicMaterial", typeof(PhysicMaterial) },
		{ "PhysicsMaterial2D", typeof(UnityEngine.PhysicsMaterial2D) },
		{ "Prefab", typeof(GameObject) },
		{ "RenderTexture", typeof(RenderTexture) },
		{ "Scene", typeof(UnityEditor.SceneAsset) },
		{ "Script", typeof(MonoScript) },
		{ "ScriptableObject", typeof(ScriptableObject) },
		{ "Shader", typeof(Shader) },
		{ "SkinnedMeshRenderer", typeof(SkinnedMeshRenderer) },
		{ "Sprite", typeof(Sprite) },
		{ "Terrain", typeof(Terrain) },
		{ "TerrainData", typeof(TerrainData) },
		{ "Texture", typeof(Texture) },
		{ "Texture2D", typeof(Texture2D) },
		{ "Texture3D", typeof(Texture3D) },
		{ "その他", typeof(Object) },
	};

	private void DrawBookmarkView()
	{
		EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

		// --- 表示モード切り替えUI ---
		EditorGUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		EditorGUILayout.LabelField("表示モード", GUILayout.Width(70));
		displayMode = (BookmarkDisplayMode)EditorGUILayout.EnumPopup(displayMode, GUILayout.Width(100));
		EditorGUILayout.EndHorizontal();

		// --- 絞り込みUI ---
		EditorGUILayout.LabelField("絞り込み", EditorStyles.boldLabel);

		// グループ絞り込み
		var filterGroupOptions = new List<string> { "すべて", "未分類" };
		filterGroupOptions.AddRange(customGroups.Where(g => !string.IsNullOrEmpty(g)));
		int filterGroupIndex = filterGroupOptions.IndexOf(filterGroup);
		if (filterGroupIndex < 0) filterGroupIndex = 0;
		filterGroupIndex = EditorGUILayout.Popup("グループ", filterGroupIndex, filterGroupOptions.ToArray());
		filterGroup = filterGroupOptions[filterGroupIndex];

		// アセット型絞り込み
		var typeNames = assetTypeOptions.Keys.ToList();
		int filterTypeIndex = assetTypeOptions.Values.ToList().IndexOf(filterType);
		if (filterTypeIndex < 0) filterTypeIndex = 0;
		filterTypeIndex = EditorGUILayout.Popup("アセット種類", filterTypeIndex, typeNames.ToArray());
		filterType = assetTypeOptions[typeNames[filterTypeIndex]];

		EditorGUILayout.Space();

		EditorGUILayout.LabelField("登録されたアセット", EditorStyles.boldLabel);
		scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

		if (displayMode == BookmarkDisplayMode.Grouped)
		{
			// --- 既存のグループ表示ロジック ---
			var allGroups = new List<string>();
			allGroups.Add("未分類");
			allGroups.AddRange(customGroups.Where(g => !string.IsNullOrEmpty(g)));
			allGroups.AddRange(bookmarks.Select(b => b.group).Where(g => !string.IsNullOrEmpty(g) && !customGroups.Contains(g)));
			allGroups = allGroups.Distinct().ToList();

			foreach (var groupName in allGroups)
			{
				// --- グループ絞り込み ---
				if (filterGroup != "すべて" && groupName != filterGroup) continue;

				if (!groupFoldouts.ContainsKey(groupName))
					groupFoldouts[groupName] = true;
				if (!groupDragAreaVisible.ContainsKey(groupName))
					groupDragAreaVisible[groupName] = true; // default: show drag area

				EditorGUILayout.BeginHorizontal();
				groupFoldouts[groupName] = EditorGUILayout.Foldout(groupFoldouts[groupName], groupName, true);
				EditorGUILayout.EndHorizontal();

				// --- 変更: グループが開いている場合のみドラッグ枠を表示 ---
				if (groupFoldouts[groupName])
				{
					Rect groupDropArea = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
					GUI.Box(groupDropArea, GUIContent.none);
					GUI.Label(groupDropArea, $"ここにアセットをドラッグで「{groupName}」に割り当て", EditorStyles.centeredGreyMiniLabel);

					Event evt = Event.current;
					if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && groupDropArea.Contains(evt.mousePosition))
					{
						DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
						if (evt.type == EventType.DragPerform)
						{
							DragAndDrop.AcceptDrag();
							foreach (Object draggedObj in DragAndDrop.objectReferences)
							{
								var entry = bookmarks.FirstOrDefault(b => b.asset == draggedObj);
								if (entry != null)
								{
									entry.group = groupName == "未分類" ? "" : groupName;
								}
								else
								{
									bookmarks.Add(new BookmarkEntry { name = draggedObj.name, asset = draggedObj, group = groupName == "未分類" ? "" : groupName });
								}
							}
							evt.Use();
						}
					}
				}

				var groupBookmarks = groupName == "未分類"
					? bookmarks.Where(b => string.IsNullOrEmpty(b.group)).Reverse().ToList()
					: bookmarks.Where(b => b.group == groupName).Reverse().ToList();

				// --- アセット型絞り込み ---
				if (filterType != null)
				{
					groupBookmarks = groupBookmarks
						.Where(b => b.asset != null && filterType.IsAssignableFrom(b.asset.GetType()))
						.ToList();
				}

				if (groupFoldouts[groupName])
				{
					for (int i = 0; i < groupBookmarks.Count; i++)
					{
						var bookmark = groupBookmarks[i];
						int globalIndex = bookmarks.IndexOf(bookmark);

						Rect itemRect = EditorGUILayout.BeginHorizontal();

						// --- Begin: Drag背景ハイライト ---
						bool isDragging = (dragSourceIndex == globalIndex);
						bool isDragOver = (dragTargetIndex == globalIndex);

						Color prevColor = GUI.backgroundColor;
						if (isDragging)
							GUI.backgroundColor = DragHighlightColor;
						else if (isDragOver)
							GUI.backgroundColor = DragOverColor;

						GUI.Box(itemRect, GUIContent.none); // 背景を描画

						GUI.backgroundColor = prevColor;
						// --- End: Drag背景ハイライト ---

						// --- Begin: テキスト色変更 ---
						bool isMouseOver = itemRect.Contains(Event.current.mousePosition);
						GUIStyle textStyle = new GUIStyle(EditorStyles.label);
						textStyle.normal.textColor = isMouseOver ? HoverTextColor : NormalTextColor;
						// --- End: テキスト色変更 ---

						// --- インデント追加 ---
						GUILayout.Space(30);

						if (GUILayout.Button("Open", GUILayout.Width(42)))
						{
							Selection.activeObject = bookmark.asset;
							EditorGUIUtility.PingObject(bookmark.asset);
						}

						// --- アセット名表示 ---
						bookmark.asset = EditorGUILayout.ObjectField(bookmark.asset, typeof(Object), false);


						if (GUILayout.Button("×", GUILayout.Width(22)))
						{
							bookmarks.Remove(bookmark);
							EditorGUILayout.EndHorizontal();
							break;
						}

						GUILayout.Space(30);

						EditorGUILayout.EndHorizontal();

						// ドラッグ＆ドロップ処理
						if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
						{
							dragSourceIndex = globalIndex;
							DragAndDrop.PrepareStartDrag();
							DragAndDrop.objectReferences = new Object[] { bookmark.asset };
							DragAndDrop.StartDrag("BookmarkDrag");
							Event.current.Use();
						}
						if (Event.current.type == EventType.DragUpdated && itemRect.Contains(Event.current.mousePosition))
						{
							DragAndDrop.visualMode = DragAndDropVisualMode.Move;
							dragTargetIndex = globalIndex;
							Event.current.Use();
						}
						if (Event.current.type == EventType.DragPerform && itemRect.Contains(Event.current.mousePosition))
						{
							DragAndDrop.AcceptDrag();
							if (dragSourceIndex != -1 && dragTargetIndex != -1 && dragSourceIndex != dragTargetIndex)
							{
								var moved = bookmarks[dragSourceIndex];
								bookmarks.RemoveAt(dragSourceIndex);
								bookmarks.Insert(dragTargetIndex, moved);
							}
							dragSourceIndex = -1;
							dragTargetIndex = -1;
							Event.current.Use();
						}
						// ドラッグ終了時にハイライトをリセット
						if (Event.current.type == EventType.MouseUp)
						{
							dragSourceIndex = -1;
							dragTargetIndex = -1;
						}
					}
				}

				// グループごとに追加スペースを確保
				EditorGUILayout.Space(20);
			}
		}
		else // Flat（リスト）表示
		{
			// --- フラット表示: 絞り込み適用 ---
			var filteredBookmarks = bookmarks
				.Where(b =>
					(filterGroup == "すべて" || (filterGroup == "未分類" ? string.IsNullOrEmpty(b.group) : b.group == filterGroup)) &&
					(filterType == null || (b.asset != null && filterType.IsAssignableFrom(b.asset.GetType())))
				)
				.Reverse()
				.ToList();

			for (int i = 0; i < filteredBookmarks.Count; i++)
			{
				var bookmark = filteredBookmarks[i];
				int globalIndex = bookmarks.IndexOf(bookmark);

				Rect itemRect = EditorGUILayout.BeginHorizontal();

				// --- Drag背景・テキスト色などは既存のまま ---
				bool isDragging = (dragSourceIndex == globalIndex);
				bool isDragOver = (dragTargetIndex == globalIndex);

				Color prevColor = GUI.backgroundColor;
				if (isDragging)
					GUI.backgroundColor = DragHighlightColor;
				else if (isDragOver)
					GUI.backgroundColor = DragOverColor;

				GUI.Box(itemRect, GUIContent.none);

				GUI.backgroundColor = prevColor;

				bool isMouseOver = itemRect.Contains(Event.current.mousePosition);
				GUIStyle textStyle = new GUIStyle(EditorStyles.label);
				textStyle.normal.textColor = isMouseOver ? HoverTextColor : NormalTextColor;

				GUILayout.Space(30);

				if (GUILayout.Button("Open", GUILayout.Width(42)))
				{
					Selection.activeObject = bookmark.asset;
					EditorGUIUtility.PingObject(bookmark.asset);
				}

				bookmark.asset = EditorGUILayout.ObjectField(bookmark.asset, typeof(Object), false);

				if (GUILayout.Button("×", GUILayout.Width(22)))
				{
					bookmarks.Remove(bookmark);
					EditorGUILayout.EndHorizontal();
					break;
				}

				GUILayout.Space(30);

				EditorGUILayout.EndHorizontal();

				// --- フラット表示でもドラッグ＆ドロップによる並び替えを有効化 ---
				if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
				{
					dragSourceIndex = globalIndex;
					DragAndDrop.PrepareStartDrag();
					DragAndDrop.objectReferences = new Object[] { bookmark.asset };
					DragAndDrop.StartDrag("BookmarkDrag");
					Event.current.Use();
				}
				if (Event.current.type == EventType.DragUpdated && itemRect.Contains(Event.current.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Move;
					dragTargetIndex = globalIndex;
					Event.current.Use();
				}
				if (Event.current.type == EventType.DragPerform && itemRect.Contains(Event.current.mousePosition))
				{
					DragAndDrop.AcceptDrag();
					if (dragSourceIndex != -1 && dragTargetIndex != -1 && dragSourceIndex != dragTargetIndex)
					{
						var moved = bookmarks[dragSourceIndex];
						bookmarks.RemoveAt(dragSourceIndex);
						bookmarks.Insert(dragTargetIndex, moved);
					}
					dragSourceIndex = -1;
					dragTargetIndex = -1;
					Event.current.Use();
				}
				// ドラッグ終了時にハイライトをリセット
				if (Event.current.type == EventType.MouseUp)
				{
					dragSourceIndex = -1;
					dragTargetIndex = -1;
				}
			}
		}

		EditorGUILayout.Space(50);
		EditorGUILayout.EndScrollView();

		// 下部にスペースを追加して新規追加UIを下端に
		GUILayout.FlexibleSpace();

		// 下部：新規ブックマーク追加
		EditorGUILayout.Space(10);
		EditorGUILayout.LabelField("新規ブックマーク追加", EditorStyles.boldLabel);

		List<string> groupOptions = new List<string> { "未分類" };
		groupOptions.AddRange(customGroups.Where(g => !string.IsNullOrEmpty(g)));

		int selectedGroupIndex = 0;
		if (!string.IsNullOrEmpty(newGroup))
		{
			selectedGroupIndex = groupOptions.IndexOf(newGroup);
			if (selectedGroupIndex < 0) selectedGroupIndex = 0;
		}

		selectedGroupIndex = EditorGUILayout.Popup("グループ", selectedGroupIndex, groupOptions.ToArray());
		newGroup = groupOptions[selectedGroupIndex] == "未分類" ? "" : groupOptions[selectedGroupIndex];

		// ウィンドウサイズに応じて高さを調整
		float minHeight = 1f;
		float maxHeight = 200f;
		float dynamicHeight = Mathf.Clamp(position.height * 0.13f, minHeight, maxHeight);

		// ウィンドウサイズに応じてフォントサイズを調整
		int minFontSize = 8;
		int maxFontSize = 16;
		int dynamicFontSize = Mathf.Clamp(Mathf.RoundToInt(position.height * 0.04f), minFontSize, maxFontSize);

		Rect dropArea = GUILayoutUtility.GetRect(0, dynamicHeight, GUILayout.ExpandWidth(true));

		GUIStyle centerBoldLargeStyle = new GUIStyle(GUI.skin.label)
		{
			alignment = TextAnchor.MiddleCenter,
			fontStyle = FontStyle.Bold,
			fontSize = dynamicFontSize
		};

		GUI.Box(dropArea, GUIContent.none);
		GUI.Label(dropArea, "ここにアセットをドラッグ＆ドロップ", centerBoldLargeStyle);

		Event evt2 = Event.current;
		if (evt2.type == EventType.DragUpdated || evt2.type == EventType.DragPerform)
		{
			if (dropArea.Contains(evt2.mousePosition))
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				if (evt2.type == EventType.DragPerform)
				{
					DragAndDrop.AcceptDrag();
					foreach (Object draggedObj in DragAndDrop.objectReferences)
					{
						if (draggedObj != null && !bookmarks.Any(b => b.asset == draggedObj))
						{
							bookmarks.Add(new BookmarkEntry { name = draggedObj.name, asset = draggedObj, group = newGroup });
						}
					}
					evt2.Use();
				}
			}
		}

		EditorGUILayout.EndVertical();
	}

	private void DrawGroupCreate()
	{
		EditorGUILayout.LabelField("グループ編集", EditorStyles.boldLabel);

		EditorGUILayout.Space(5);

		EditorGUILayout.LabelField("新規グループ追加", EditorStyles.boldLabel);
		newGroupName = EditorGUILayout.TextField("グループ名", newGroupName);
		if (!string.IsNullOrEmpty(newGroupName) && GUILayout.Button("グループ追加"))
		{
			if (!customGroups.Contains(newGroupName))
			{
				customGroups.Add(newGroupName);
				// SaveBookmarks(); ← 削除
			}
			newGroupName = "";
			GUI.FocusControl(null); // フォーカスを外してテキストをクリア
		}

		EditorGUILayout.Space(5);

		// 既存グループ一覧と削除ボタン
		EditorGUILayout.LabelField("既存グループ一覧", EditorStyles.miniBoldLabel);
		Event evt = Event.current;
		for (int i = 0; i < customGroups.Count; i++)
		{
			Rect groupRect = EditorGUILayout.BeginHorizontal();

			// --- Begin: Drag背景ハイライト ---
			bool isDragging = (groupDragSourceIndex == i);
			bool isDragOver = (groupDragTargetIndex == i);

			Color prevColor = GUI.backgroundColor;
			if (isDragging)
				GUI.backgroundColor = DragHighlightColor;
			else if (isDragOver)
				GUI.backgroundColor = DragOverColor;

			GUI.Box(groupRect, GUIContent.none);

			GUI.backgroundColor = prevColor;
			// --- End: Drag背景ハイライト ---

			// --- Begin: テキスト色変更 ---
			bool isMouseOver = groupRect.Contains(Event.current.mousePosition);
			GUIStyle textStyle = new GUIStyle(EditorStyles.label);
			textStyle.normal.textColor = isMouseOver ? HoverTextColor : NormalTextColor;
			// --- End: テキスト色変更 ---

			// --- リネームUI ---
			if (renameGroupIndex == i)
			{
				renameGroupName = EditorGUILayout.TextField(renameGroupName, GUILayout.MinWidth(100));
				if (GUILayout.Button("保存", GUILayout.Width(60)))
				{
					string oldName = customGroups[i];
					string newName = renameGroupName.Trim();
					if (!string.IsNullOrEmpty(newName) && !customGroups.Contains(newName))
					{
						// グループ名変更
						customGroups[i] = newName;
						// ブックマークのgroupも更新
						foreach (var b in bookmarks.Where(b => b.group == oldName))
							b.group = newName;
						// SaveBookmarks(); ← 削除
					}
					renameGroupIndex = -1;
					renameGroupName = "";
					GUI.FocusControl(null);
				}
				if (GUILayout.Button("キャンセル", GUILayout.Width(60)))
				{
					renameGroupIndex = -1;
					renameGroupName = "";
					GUI.FocusControl(null);
				}
			}
			else
			{
				EditorGUILayout.LabelField(customGroups[i], textStyle, GUILayout.MinWidth(100));
				if (GUILayout.Button("リネーム", GUILayout.Width(60)))
				{
					renameGroupIndex = i;
					renameGroupName = customGroups[i];
					GUI.FocusControl(null);
				}
			}

			if (GUILayout.Button("削除", GUILayout.Width(40)))
			{
				string groupToRemove = customGroups[i];
				var hasBookmarks = bookmarks.Any(b => b.group == groupToRemove);

				bool canRemove = true;
				if (hasBookmarks)
				{
					canRemove = EditorUtility.DisplayDialog(
						"グループ削除の確認",
						$"グループ「{groupToRemove}」にはブックマークが存在します。\n削除するとこれらは「未分類」になります。\n本当に削除しますか？",
						"はい",
						"キャンセル"
					);
				}

				if (canRemove)
				{
					foreach (var b in bookmarks.Where(b => b.group == groupToRemove))
					{
						b.group = "";
					}
					customGroups.RemoveAt(i);
					i--;
					// SaveBookmarks(); ← 削除
					newGroupName = "";
					GUI.FocusControl(null);
					EditorGUILayout.EndHorizontal();
					continue;
				}
			}
			EditorGUILayout.EndHorizontal();

			// ドラッグ＆ドロップ処理
			if (evt.type == EventType.MouseDown && groupRect.Contains(evt.mousePosition))
			{
				groupDragSourceIndex = i;
				DragAndDrop.PrepareStartDrag();
				DragAndDrop.SetGenericData("GroupDrag", customGroups[i]);
				DragAndDrop.StartDrag("GroupDrag");
				evt.Use();
			}
			if (evt.type == EventType.DragUpdated && groupRect.Contains(evt.mousePosition))
			{
				if (DragAndDrop.GetGenericData("GroupDrag") != null)
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Move;
					groupDragTargetIndex = i;
					evt.Use();
				}
			}
			if (evt.type == EventType.DragPerform && groupRect.Contains(evt.mousePosition))
			{
				if (groupDragSourceIndex != -1 && groupDragTargetIndex != -1 && groupDragSourceIndex != groupDragTargetIndex)
				{
					string moved = customGroups[groupDragSourceIndex];
					customGroups.RemoveAt(groupDragSourceIndex);
					customGroups.Insert(groupDragTargetIndex, moved);
				}
				groupDragSourceIndex = -1;
				groupDragTargetIndex = -1;
				DragAndDrop.AcceptDrag();
				DragAndDrop.SetGenericData("GroupDrag", null);
				evt.Use();
			}
			// Reset highlight if drag ends
			if (evt.type == EventType.MouseUp)
			{
				groupDragSourceIndex = -1;
				groupDragTargetIndex = -1;
			}
		}
	}

	private static string GetBookmarkFilePath()
	{
		string path = Path.Combine(Directory.GetCurrentDirectory(), "Packages/BookmarkTool/Editor/BookmarkToolData.json");
		string directory = Path.GetDirectoryName(path);
		if (!Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		return path;
	}

	// 差分判定
	private bool HasDifference()
	{
		// ブックマーク差分
		if (bookmarks.Count != savedBookmarks.Count ||
			bookmarks.Where((b, i) => !BookmarkEquals(b, savedBookmarks.ElementAtOrDefault(i))).Any())
			return true;

		// グループ差分
		if (customGroups.Count != savedCustomGroups.Count ||
			customGroups.Where((g, i) => g != savedCustomGroups.ElementAtOrDefault(i)).Any())
			return true;

		return false;
	}

	private bool BookmarkEquals(BookmarkEntry a, BookmarkEntry b)
	{
		if (a == null || b == null) return false;
		return a.name == b.name && a.asset == b.asset && a.group == b.group;
	}

	private void SaveBookmarks()
	{
		// 保存前にパスをセット
		foreach (var b in bookmarks)
		{
			b.assetPath = b.asset != null ? AssetDatabase.GetAssetPath(b.asset) : "";
		}

		var wrapper = new BookmarkListWrapper
		{
			list = bookmarks,
			customGroups = customGroups,
			displayMode = displayMode.ToString(),
			filterGroup = filterGroup,
			filterTypeName = filterType != null ? filterType.AssemblyQualifiedName : ""
		};
		string json = JsonUtility.ToJson(wrapper);

		// --- 圧縮して保存 ---
		string path = GetBookmarkFilePath();
		using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
		using (var gz = new GZipStream(fs, CompressionMode.Compress))
		using (var sw = new StreamWriter(gz))
		{
			sw.Write(json);
		}
		AssetDatabase.Refresh();

		savedBookmarks = bookmarks.Select(b => new BookmarkEntry { name = b.name, asset = b.asset, group = b.group, assetPath = b.assetPath }).ToList();
		savedCustomGroups = new List<string>(customGroups);
	}

	private void LoadBookmarks()
	{
		string path = GetBookmarkFilePath();
		if (File.Exists(path))
		{
			string json;
			// --- 圧縮データを展開して読込 ---
			using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
			using (var gz = new GZipStream(fs, CompressionMode.Decompress))
			using (var sr = new StreamReader(gz))
			{
				json = sr.ReadToEnd();
			}

			var wrapper = JsonUtility.FromJson<BookmarkListWrapper>(json);
			bookmarks = wrapper?.list ?? new List<BookmarkEntry>();
			customGroups = wrapper?.customGroups ?? new List<string>();

			// アセット参照を復元
			foreach (var b in bookmarks)
			{
				if (!string.IsNullOrEmpty(b.assetPath))
				{
					b.asset = AssetDatabase.LoadAssetAtPath<Object>(b.assetPath);
				}
				else
				{
					b.asset = null;
				}
			}

			if (!string.IsNullOrEmpty(wrapper?.displayMode) && Enum.TryParse(wrapper.displayMode, out BookmarkDisplayMode mode))
				displayMode = mode;
			filterGroup = wrapper?.filterGroup ?? "すべて";
			filterType = !string.IsNullOrEmpty(wrapper?.filterTypeName)
				? Type.GetType(wrapper.filterTypeName)
				: null;

			savedBookmarks = bookmarks.Select(b => new BookmarkEntry { name = b.name, asset = b.asset, group = b.group, assetPath = b.assetPath }).ToList();
			savedCustomGroups = new List<string>(customGroups);
		}
		else
		{
			bookmarks = new List<BookmarkEntry>();
			customGroups = new List<string>();
			savedBookmarks = new List<BookmarkEntry>();
			savedCustomGroups = new List<string>();
			displayMode = BookmarkDisplayMode.Flat;
			filterGroup = "すべて";
			filterType = null;
		}
	}

	[System.Serializable]
	class BookmarkListWrapper
	{
		public List<BookmarkEntry> list;
		public List<string> customGroups;
		public string displayMode;
		public string filterGroup;
		public string filterTypeName;
	}

	private void OnEnable() => LoadBookmarks();
	private void OnDisable() => SaveBookmarks();

	// --- Add: Context menu integration ---
#if UNITY_EDITOR
    // Register right-click menu for assets in Project window
    [InitializeOnLoadMethod]
    private static void RegisterContextMenu()
    {
        // No-op: Just triggers static constructor
    }

    // Add menu item to Project window context menu
    [MenuItem("Assets/ブックマークに追加", false, 2000)]
    private static void AddSelectedAssetsToBookmark()
    {
        // Get selected assets
        var selected = Selection.objects;
        if (selected == null || selected.Length == 0) return;

        // Find or create the BookmarkTool window
        var window = GetWindow<BookmarkTool>("Bookmark Tool");
        if (window == null) return;

        foreach (var obj in selected)
        {
            if (obj == null) continue;
            // Avoid duplicates
            if (window.bookmarks.Any(b => b.asset == obj)) continue;
            window.bookmarks.Add(new BookmarkEntry
            {
                name = obj.name,
                asset = obj,
                group = "" // Default to ungrouped
            });
            Debug.Log($"ブックマークに追加: {obj.name} ({AssetDatabase.GetAssetPath(obj)})");
        }
        window.Repaint();
    }

    // Validate menu item: only show if selection is valid
    [MenuItem("Assets/ブックマークに追加", true)]
    private static bool ValidateAddSelectedAssetsToBookmark()
    {
        var selected = Selection.objects;
        return selected != null && selected.Any(obj => obj != null);
    }
#endif
}

#endif // UNITY_EDITOR