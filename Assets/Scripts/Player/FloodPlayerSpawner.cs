using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Generic;
using UnityEngine;

public class FloodPlayerSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject playerPrefab; // Префаб игрока с NetworkObject
    [SerializeField] private Transform[] customSpawnPoints; // Список всех возможных
    private List<Transform> _availableSpawnPoints;          // Список доступных точек спауна
    // Этот метод вызывается на сервере, когда клиент загрузил стартовую сцену
    public override void OnStartServer()
    {
        base.OnStartServer();
        _availableSpawnPoints = new List<Transform>(customSpawnPoints); // Загружаем все точки спауна в список доступных
    }
    public override void OnSpawnServer(NetworkConnection connection)
    {
        base.OnSpawnServer(connection);
        if (_availableSpawnPoints.Count == 0)
            Debug.LogError("Нет доступных точек спауна!");
        else
            SpawnPlayer(connection);
    }
    private void SpawnPlayer(NetworkConnection connection)
    {
        if (playerPrefab == null || customSpawnPoints == null || customSpawnPoints.Length == 0)
        {
            Debug.LogError("Эй! Ты забыл привязать префаб или точки спауна в Инспекторе!");
            return;
        }
        // Выбираем случайную точку из нашего массива доступных точек
        int spawnIndex = UnityEngine.Random.Range(0, _availableSpawnPoints.Count);
        Vector3 spawnPos = _availableSpawnPoints[spawnIndex].position;
        Quaternion spawnRot = _availableSpawnPoints[spawnIndex].rotation;
        // В FishNet объекты из пула создаются через NetworkManager
        NetworkObject spawnedPlayer = NetworkManager.GetPooledInstantiated(playerPrefab, spawnPos, spawnRot, true);
        // Обращаемся к ServerManager, чтобы заспаунить в сети
        ServerManager.Spawn(spawnedPlayer, connection);
        Debug.LogFormat($"Player spawned in spawn point {spawnIndex}");
        // Удаляем использованную точку спауна
        _availableSpawnPoints.RemoveAt(spawnIndex);
    }
}
