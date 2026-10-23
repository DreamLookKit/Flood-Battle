using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

public class FloodPlayerSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject playerPrefab; // Твой префаб игрока с NetworkObject
    [SerializeField] private Transform[] customSpawnPoints; // Твои крутые точки спауна
    // Этот метод вызывается на сервере, когда клиент загрузил стартовую сцену
    public override void OnSpawnServer(NetworkConnection connection)
    {
        base.OnSpawnServer(connection);
        SpawnPlayer(connection);
    }
    private void SpawnPlayer(NetworkConnection connection)
    {
        if (playerPrefab == null || customSpawnPoints == null || customSpawnPoints.Length == 0)
        {
            Debug.LogError("Эй! Ты забыл привязать префаб или точки спауна в Инспекторе!");
            return;
        }
        // Выбираем случайную точку из нашего массива
        int randomIndex = Random.Range(0, customSpawnPoints.Length);
        Vector3 spawnPos = customSpawnPoints[randomIndex].position;
        Quaternion spawnRot = customSpawnPoints[randomIndex].rotation;
        // В FishNet объекты из пула создаются через NetworkManager
        NetworkObject spawnedPlayer = NetworkManager.GetPooledInstantiated(playerPrefab, spawnPos, spawnRot, true);
        // Обращаемся к ServerManager, чтобы заспаунить в сети
        ServerManager.Spawn(spawnedPlayer, connection);
    }
}
