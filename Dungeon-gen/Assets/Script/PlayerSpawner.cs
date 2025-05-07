using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Generation;
using DungeonGen.Core;

namespace DungeonGen
{
    /// <summary>
    /// ダンジョン内にプレイヤーキャラクターを生成・管理するコンポーネント
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("プレイヤー設定")]
        [Tooltip("生成するThirdPersonキャラクターのプレファブ")]
        public GameObject playerPrefab;

        [Tooltip("プレイヤーのスケール (1.0が元のサイズ)")]
        [Range(0.1f, 1.0f)]
        public float playerScale = 0.3f;

        [Tooltip("プレイヤーの高さオフセット（床からの高さ）")]
        public float heightOffset = 1.5f;

        [Header("生成設定")]
        [Tooltip("最初の部屋に生成する場合はチェック、ランダムな部屋ならオフ")]
        public bool spawnInFirstRoom = true;

        [Tooltip("部屋の中央ではなく、安全なスポットを探す")]
        public bool findSafeSpot = true;

        // 現在のプレイヤーインスタンス
        private GameObject _playerInstance;

        /// <summary>
        /// 部屋リストを基にプレイヤーを生成
        /// </summary>
        // SpawnPlayerメソッドを修正
        public void SpawnPlayer(List<RectInt> rooms, CellMap map)
        {
            if (rooms == null || rooms.Count == 0)
            {
                Debug.LogError("部屋が存在しないため、プレイヤーを生成できません。");
                return;
            }

            Debug.Log($"プレイヤー生成/配置処理開始: 部屋数={rooms.Count}");

            // 既存のキャラクターを検索
            GameObject existingPlayer = GameObject.Find("NestedParentArmature_Unpack");

            if (existingPlayer != null)
            {
                Debug.Log($"既存のプレイヤーキャラクターが見つかりました: {existingPlayer.name}");
                // 既存のプレイヤーを使用
                _playerInstance = existingPlayer;
            }
            else if (playerPrefab != null)
            {
                Debug.Log("既存のキャラクターが見つからないため、新しく生成します");
                // 既存のプレイヤーインスタンスを削除
                if (_playerInstance != null)
                {
                    Debug.Log("既存のプレイヤーを削除します");
                    Destroy(_playerInstance);
                }

                // 部屋を選択（最初またはランダム）
                int roomIndex = spawnInFirstRoom ? 0 : Random.Range(0, rooms.Count);
                RectInt selectedRoom = rooms[roomIndex];

                Debug.Log($"部屋{roomIndex}を選択: 位置({selectedRoom.x}, {selectedRoom.y}), サイズ({selectedRoom.width}x{selectedRoom.height})");

                // スポーン位置を決定
                Vector3 spawnPosition;
                if (findSafeSpot)
                {
                    // 部屋内の安全なスポットを検索
                    Vector2Int safeSpot = FindSafeSpotInRoom(selectedRoom, map);
                    spawnPosition = new Vector3(safeSpot.x, heightOffset, safeSpot.y);
                    Debug.Log($"安全なスポットを選択: ({safeSpot.x}, {safeSpot.y}), 高さ={heightOffset}");
                }
                else
                {
                    // 部屋の中央に配置
                    spawnPosition = new Vector3(
                        selectedRoom.center.x,
                        heightOffset,
                        selectedRoom.center.y
                    );
                    Debug.Log($"部屋の中央を選択: ({selectedRoom.center.x}, {selectedRoom.center.y}), 高さ={heightOffset}");
                }

                // 生成位置をデバッグ表示
                // StartCoroutine(CreateVisualMarker(spawnPosition));

                // プレイヤーを生成
                _playerInstance = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

                if (_playerInstance == null)
                {
                    Debug.LogError("プレイヤーの生成に失敗しました");
                    return;
                }

                // プレイヤーの親を設定せずシーンルートに配置（重要）
                _playerInstance.transform.parent = null;

                // プレイヤーの名前を設定
                _playerInstance.name = "Player_Character";
            }
            else
            {
                Debug.LogError("プレイヤープレファブが設定されておらず、既存のキャラクターも見つかりません。");
                return;
            }

            // 共通処理: プレイヤー位置の設定
            // 部屋を選択（最初またはランダム）
            int targetRoomIndex = spawnInFirstRoom ? 0 : Random.Range(0, rooms.Count);
            RectInt targetRoom = rooms[targetRoomIndex];

            // 配置位置を決定
            Vector3 targetPosition;
            if (findSafeSpot)
            {
                // 部屋内の安全なスポットを検索
                Vector2Int safeSpot = FindSafeSpotInRoom(targetRoom, map);
                targetPosition = new Vector3(safeSpot.x, heightOffset, safeSpot.y);
                Debug.Log($"安全なスポットを選択: ({safeSpot.x}, {safeSpot.y}), 高さ={heightOffset}");
            }
            else
            {
                // 部屋の中央に配置
                targetPosition = new Vector3(
                    targetRoom.center.x,
                    heightOffset,
                    targetRoom.center.y
                );
                Debug.Log($"部屋の中央を選択: ({targetRoom.center.x}, {targetRoom.center.y}), 高さ={heightOffset}");
            }

            // 配置位置をデバッグ表示
            StartCoroutine(CreateVisualMarker(targetPosition));

            // CharacterControllerを一時的に無効化して位置を設定
            var characterController = _playerInstance.GetComponent<CharacterController>();
            if (characterController != null)
            {
                bool wasEnabled = characterController.enabled;
                characterController.enabled = false;
                _playerInstance.transform.position = targetPosition;
                characterController.enabled = wasEnabled;
            }
            else
            {
                _playerInstance.transform.position = targetPosition;
            }

            // スケールを調整
            _playerInstance.transform.localScale = Vector3.one * playerScale;

            // キャラクターコントローラーの調整
            AdjustCharacterController(_playerInstance);

            // プレイヤーの全コンポーネントを有効化
            ActivateAllComponents(_playerInstance);

            // カメラのセットアップ
            SetupCamera(_playerInstance);

            // 入力システムの有効化確認
            EnsureInputSystemActive(_playerInstance);

            Debug.Log($"プレイヤー配置完了! 名前: {_playerInstance.name}, 位置: {_playerInstance.transform.position}, スケール: {playerScale}");

            // プレイヤーとカメラの位置を強制更新
            StartCoroutine(DelayedPositionUpdate(_playerInstance));
        }

        // すべてのコンポーネントを有効化
        private void ActivateAllComponents(GameObject player)
        {
            // プレイヤー自身を確実にアクティブに
            player.SetActive(true);

            // すべての子オブジェクトをアクティブに
            foreach (Transform child in player.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            // 重要なコンポーネントが無効化されていないか確認
            var characterController = player.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = true;
                Debug.Log("CharacterControllerを有効化しました");
            }

            var animator = player.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.enabled = true;
                Debug.Log("Animatorを有効化しました");
            }

            // レンダラーの確認
            var renderers = player.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("プレイヤーにRendererコンポーネントが見つかりません！");
            }
            else
            {
                Debug.Log($"{renderers.Length}個のRendererコンポーネントを確認");
                foreach (var renderer in renderers)
                {
                    renderer.enabled = true;
                }
            }
        }

        // 入力システムのセットアップ
        private void EnsureInputSystemActive(GameObject player)
        {
            // PlayerInputコンポーネントの確認
            var playerInput = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = true;
                Debug.Log("PlayerInputコンポーネントを有効化しました");

                // 接続ステータスを確認
                string actions = playerInput.actions != null ? playerInput.actions.name : "未設定";
                Debug.Log($"入力アクションアセット: {actions}");
            }
            else
            {
                Debug.LogWarning("PlayerInputコンポーネントが見つかりません。プレイヤー操作ができない可能性があります");
            }
        }

        // プレイヤーとカメラの位置を強制更新
        private IEnumerator DelayedPositionUpdate(GameObject player)
        {
            // 1フレーム待機
            yield return null;

            if (player != null)
            {
                // プレイヤーの位置をわずかに更新して動きを検出させる
                player.transform.position += Vector3.up * 0.01f;

                // カメラの追従を強制
                var virtualCamera = FindObjectOfType<Cinemachine.CinemachineVirtualCamera>();
                if (virtualCamera != null)
                {
                    virtualCamera.OnTargetObjectWarped(player.transform, player.transform.position);
                }

                var freeLookCamera = FindObjectOfType<Cinemachine.CinemachineFreeLook>();
                if (freeLookCamera != null)
                {
                    freeLookCamera.OnTargetObjectWarped(player.transform, player.transform.position);
                }

                Debug.Log("プレイヤーとカメラの位置を強制更新しました");
            }
        }

        // 生成位置を視覚的に確認するためのマーカーを作成
        private IEnumerator CreateVisualMarker(Vector3 position)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * 0.5f;
            marker.GetComponent<Renderer>().material.color = Color.red;

            yield return new WaitForSeconds(5.0f);

            if (marker != null)
                Destroy(marker);
        }

        // プレイヤーが地面の上に確実に位置するようにする
        private IEnumerator EnsurePlayerAboveGround(GameObject player)
        {
            yield return new WaitForSeconds(0.2f);

            if (player != null)
            {
                // 現在位置から下方向にレイキャスト
                RaycastHit hit;
                if (Physics.Raycast(player.transform.position + Vector3.up, Vector3.down, out hit, 10f))
                {
                    // 床が見つかった場合、その上に配置
                    float floorY = hit.point.y;
                    Vector3 newPos = player.transform.position;
                    newPos.y = floorY + (1.0f * playerScale);
                    player.transform.position = newPos;
                    Debug.Log($"プレイヤー位置を調整: 床の高さ={floorY}, 新しい位置Y={newPos.y}");
                }
                else
                {
                    // 床が見つからない場合は少し持ち上げる
                    Vector3 newPos = player.transform.position;
                    newPos.y += 0.5f;
                    player.transform.position = newPos;
                    Debug.Log("床が検出できません。プレイヤー位置を少し持ち上げました。");
                }
            }
        }

        private void AdjustCharacterController(GameObject player)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                // キャラクターコントローラーのスキンの幅を調整
                controller.skinWidth = 0.01f * playerScale;

                // 床からの最小距離を設定
                controller.stepOffset = 0.3f * playerScale;

                // コントローラーの中心位置を調整
                controller.center = new Vector3(0, 1.0f * playerScale, 0);

                Debug.Log("CharacterControllerのパラメータを調整しました");
            }
            else
            {
                Debug.LogWarning("CharacterControllerコンポーネントが見つかりません");
            }
        }

        /// <summary>
        /// 部屋内の安全な（壁や障害物から離れた）スポットを探す
        /// </summary>
        private Vector2Int FindSafeSpotInRoom(RectInt room, CellMap map)
        {
            // 部屋の中央から開始
            Vector2Int center = Vector2Int.RoundToInt(room.center);

            // 中央が使えるならそこを使用
            if (IsSafeLocation(center, map))
            {
                Debug.Log("部屋の中央が安全なスポットとして使用されます");
                return center;
            }

            Debug.Log("部屋の中央は安全でないため、別の場所を探します");

            // 中央から徐々に離れていく同心円状に安全なスポットを探す
            int maxRadius = Mathf.Min(room.width, room.height) / 2;

            for (int radius = 1; radius < maxRadius; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        // 同心円状の範囲内のみチェック
                        if (Mathf.Abs(x) == radius || Mathf.Abs(y) == radius)
                        {
                            Vector2Int testPos = center + new Vector2Int(x, y);

                            // 部屋内かつ安全な場所ならそこを使用
                            if (room.Contains(testPos) && IsSafeLocation(testPos, map))
                            {
                                Debug.Log($"安全なスポットを見つけました: ({testPos.x}, {testPos.y})、中央から距離: {radius}");
                                return testPos;
                            }
                        }
                    }
                }
            }

            // 安全な場所が見つからない場合は中央を返す
            Debug.LogWarning("安全なスポットが見つかりませんでした。中央を使用します。");
            return center;
        }

        /// <summary>
        /// 指定位置が安全かどうか確認（壁や障害物の有無）
        /// </summary>
        private bool IsSafeLocation(Vector2Int position, CellMap map)
        {
            // マップ範囲内か確認
            if (!map.InBounds(position.x, position.y))
            {
                return false;
            }

            // セルが部屋または通路か確認
            CellType cellType = map.Cells[position.x, position.y].Type;
            if (cellType != CellType.Room && cellType != CellType.Corridor)
            {
                return false;
            }

            // 上下左右のセルが全て壁でないことを確認（狭い場所を避ける）
            foreach (Vector2Int dir in new Vector2Int[]
            {
                Vector2Int.up, Vector2Int.down,
                Vector2Int.left, Vector2Int.right
            })
            {
                Vector2Int neighbor = position + dir;
                if (!map.InBounds(neighbor.x, neighbor.y) ||
                    map.Cells[neighbor.x, neighbor.y].Type == CellType.Empty)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// プレイヤー用カメラのセットアップ
        /// </summary>
        private void SetupCamera(GameObject player)
        {
            // FreeLookカメラの場合（Third Person Controllerで一般的）
            var freeLookCamera = FindObjectOfType<Cinemachine.CinemachineFreeLook>();
            if (freeLookCamera != null)
            {
                freeLookCamera.Follow = player.transform;

                // PlayerCameraRootを探す
                Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
                if (cameraRoot != null)
                {
                    freeLookCamera.LookAt = cameraRoot;
                }
                else
                {
                    freeLookCamera.LookAt = player.transform;
                }

                // カメラ距離を調整（近づける）
                AdjustCameraDistance(freeLookCamera, 3.0f); // 距離を3.0に設定（デフォルトより近く）

                Debug.Log("CinemachineFreeLookカメラをプレイヤーに設定し、距離を調整しました");
            }

            // Cinemachineカメラが存在する場合はターゲット設定
            var virtualCamera = FindObjectOfType<Cinemachine.CinemachineVirtualCamera>();
            if (virtualCamera != null)
            {
                virtualCamera.Follow = player.transform;

                // PlayerCameraRootを探す
                Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
                if (cameraRoot != null)
                {
                    virtualCamera.LookAt = cameraRoot;
                }
                else
                {
                    virtualCamera.LookAt = player.transform;
                }

                // 標準カメラの場合も距離を調整
                var composer = virtualCamera.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
                if (composer != null)
                {
                    composer.m_TrackedObjectOffset = new Vector3(0, 1.5f, 0);
                }

                Debug.Log("Cinemachineカメラをプレイヤーに設定しました");
            }
        }

        // カメラの距離を調整するためのヘルパーメソッドを追加
        private void AdjustCameraDistance(Cinemachine.CinemachineFreeLook freeLookCamera, float distance)
        {
            // すべてのリグに同じ距離を設定
            if (freeLookCamera != null)
            {
                // 上部、中部、下部のリグの距離をすべて設定
                freeLookCamera.m_Orbits[0].m_Radius = distance * 1.1f; // 上部
                freeLookCamera.m_Orbits[1].m_Radius = distance;        // 中部
                freeLookCamera.m_Orbits[2].m_Radius = distance * 0.9f; // 下部
            }
        }

        // インスペクターからアクセス可能にする
        [ContextMenu("現在のプレイヤーを検索")]
        public void FindCurrentPlayer()
        {
            var players = GameObject.FindGameObjectsWithTag("Player");

            if (players.Length > 0)
            {
                Debug.Log($"{players.Length}個のプレイヤーオブジェクトが見つかりました:");
                foreach (var player in players)
                {
                    Debug.Log($"- {player.name}: 位置={player.transform.position}, アクティブ={player.activeSelf}");
                }
            }
            else
            {
                Debug.Log("Playerタグのついたオブジェクトが見つかりません");

                // ThirdPersonControllerを探す
                var tpcs = FindObjectsOfType<CharacterController>();
                if (tpcs.Length > 0)
                {
                    Debug.Log($"{tpcs.Length}個のCharacterControllerが見つかりました:");
                    foreach (var tpc in tpcs)
                    {
                        Debug.Log($"- {tpc.gameObject.name}: 位置={tpc.transform.position}, アクティブ={tpc.gameObject.activeSelf}");
                    }
                }
            }
        }

        // 問題発生時に全リセット
        [ContextMenu("すべて再生成")]
        public void ResetAndSpawn()
        {
            // 既存のプレイヤーをすべて削除
            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var player in players)
            {
                DestroyImmediate(player);
            }

            var characterControllers = FindObjectsOfType<CharacterController>();
            foreach (var cc in characterControllers)
            {
                if (cc.gameObject.name.Contains("Player") || cc.gameObject.name.Contains("Character"))
                {
                    DestroyImmediate(cc.gameObject);
                }
            }

            // ダンジョン生成を実行
            var dungeonController = FindObjectOfType<Dungeon2DController>();
            if (dungeonController != null)
            {
                dungeonController.RegenerateDungeon();
            }
            else
            {
                // ダンジョンなしでプレイヤーだけ生成
                var map = new DungeonGen.Core.CellMap(10, 10);
                var room = new RectInt(2, 2, 5, 5);
                SpawnPlayer(new List<RectInt> { room }, map);
            }
        }
    }
}