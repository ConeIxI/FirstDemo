using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonManager<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _isApplicationQuitting;

    public static T Instance
    {
        get
        {
            if (_isApplicationQuitting)
            {
                return null;
            }

            if (_instance == null)
            {
                _instance = GameObject.FindObjectOfType<T>();
                if (_instance == null)
                {
                    GameObject obj = new GameObject(typeof(T).Name);
                    _instance = obj.AddComponent<T>();
                    DontDestroyOnLoad(obj);
                }
            }

            return _instance;
        }
    }

    protected bool IsSingletonInstance => _instance == this;

    public static bool TryGetInstance(out T instance)
    {
        instance = _instance;
        if (instance == null && !_isApplicationQuitting)
        {
            instance = GameObject.FindObjectOfType<T>();
        }

        return instance != null;
    }

    protected virtual void Awake()
    {
        T current = this as T;
        if (_instance != null && _instance != current)
        {
            Destroy(gameObject);
            return;
        }

        _instance = current;
        DontDestroyOnLoad(gameObject);
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _isApplicationQuitting = true;
    }
}
