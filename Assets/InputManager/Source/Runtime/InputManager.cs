#region [Copyright (c) 2014 Cristian Alexandru Geambasu]
//	Distributed under the terms of an MIT-style license:
//
//	The MIT License
//
//	Copyright (c) 2014 Cristian Alexandru Geambasu
//
//	Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
//	and associated documentation files (the "Software"), to deal in the Software without restriction, 
//	including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
//	and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
//	subject to the following conditions:
//
//	The above copyright notice and this permission notice shall be included in all copies or substantial 
//	portions of the Software.
//
//	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
//	INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
//	PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
//	FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, 
//	ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
#endregion
using UnityEngine;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace TeamUtility.IO
{
	/// <summary>
	/// Encapsulates a method that takes one parameter(the key) and returns 'true' if
	/// the key is accepted or 'false' if it isn't.
	/// The 'userData' parameter is used to send additional user data.
	/// </summary>
	public delegate bool KeyScanHandler(KeyCode key, object[] userData);
	
	/// <summary>
	/// Encapsulates a method that takes one parameter(the axis) and returns 'true' if
	/// the axis is accepted or 'false' if it isn't.
	/// The 'userData' parameter is used to send additional user data.
	/// </summary>
	public delegate bool AxisScanHandler(int axis, object[] userData);

	/// <summary>
	/// Encapsulates a method that takes one parameter(the scan result) and returns 'true' if
	/// the scan result is accepted or 'false' if it isn't.
	/// </summary>
	public delegate bool ScanHandler(ScanResult result);
	
	[ExecuteInEditMode]
	public sealed class InputManager : MonoBehaviour 
	{
		public const string VERSION = "1.8.3.0";
		
		#region [Fields]
		public event Action<string> ConfigurationChanged;
		public event Action<InputConfiguration> ConfigurationDirty;
		public event Action Loaded;
		public event Action Saved;
		public event Action RemoteUpdate;
		
		public List<InputConfiguration> inputConfigurations;
		public string defaultConfiguration;
		public bool dontDestroyOnLoad;
		public bool ignoreTimescale;
		
		private static InputManager _instance;
		private InputConfiguration _currentConfiguration;
		private ScanHandler _scanHandler;
		private ScanResult _scanResult;
		private ScanFlags _scanFlags;
		private string _cancelScanButton;
		private float _scanStartTime;
		private float _scanTimeout;
		private int _scanJoystick;
		private object _scanUserData;
		
		//	Cached data for improved performance.
		private string[] _rawMouseAxes;
		private string[] _rawJoystickAxes;
		private KeyCode[] _keys;
		private Dictionary<string, KeyCode> _stringToKeyTable;
		private Dictionary<string, InputConfiguration> _configurationTable;
		private Dictionary<string, Dictionary<string, AxisConfiguration>> _axesTable;
		
		#endregion
		
		private void OnEnable()
		{
			if(inputConfigurations == null)
			{
				inputConfigurations = new List<InputConfiguration>();
			}
		}
		
		private void Awake()
		{
#if UNITY_EDITOR
			if(!UnityEditor.EditorApplication.isPlaying)
				return;
#endif
			if(_instance != null) 
			{
				UnityEngine.Object.Destroy(this);
			}
			else
			{
				if(dontDestroyOnLoad) 
				{
					UnityEngine.Object.DontDestroyOnLoad(this);
				}
				
				_instance = this;
				_keys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
				_stringToKeyTable = new Dictionary<string, KeyCode>();
				_configurationTable = new Dictionary<string, InputConfiguration>();
				_axesTable = new Dictionary<string, Dictionary<string, AxisConfiguration>>();
				
				SetRawAxisNames();
				SetKeyNames();
				Initialize();
			}
		}
		
		private void SetRawAxisNames()
		{
			_rawMouseAxes = new string[AxisConfiguration.MaxMouseAxes];
			for(int i = 0; i < _rawMouseAxes.Length; i++)
			{
				_rawMouseAxes[i] = string.Concat("mouse_axis_", i);
			}
			
			_rawJoystickAxes = new string[AxisConfiguration.MaxJoysticks * AxisConfiguration.MaxJoystickAxes];
			for(int i = 0; i < AxisConfiguration.MaxJoysticks; i++)
			{
				for(int j = 0; j < AxisConfiguration.MaxJoystickAxes; j++)
				{
					_rawJoystickAxes[i * AxisConfiguration.MaxJoystickAxes + j] = string.Concat("joy_", i, "_axis_", j);
				}
			}
		}
		
		private void SetKeyNames()
		{
			for(int i = 0; i < _keys.Length; i++)
			{
				string keyStr = _keys[i].ToString().ToLower();
				if(!_stringToKeyTable.ContainsKey(keyStr))
					_stringToKeyTable.Add(keyStr, _keys[i]);
			}
		}
		
		private void Initialize()
		{
			if(inputConfigurations.Count == 0)
				return;
			
			PopulateLookupTables();
			if(string.IsNullOrEmpty(defaultConfiguration) || !_configurationTable.ContainsKey(defaultConfiguration))
			{
				_currentConfiguration = inputConfigurations[0];
			}
			else
			{
				_currentConfiguration = _configurationTable[defaultConfiguration];
			}
			
			foreach(InputConfiguration inputConfig in inputConfigurations)
			{
				foreach(AxisConfiguration axisConfig in inputConfig.axes)
				{
					axisConfig.Initialize();
				}
			}
			ResetInputAxes();
		}
		
		private void PopulateLookupTables()
		{
			_configurationTable.Clear();
			foreach(InputConfiguration inputConfig in inputConfigurations)
			{
				if(!_configurationTable.ContainsKey(inputConfig.name))
				{
					_configurationTable.Add(inputConfig.name, inputConfig);
				}
#if UNITY_EDITOR
				else
				{
					Debug.LogWarning("An input configuration with the name \'" + inputConfig.name + "\' already exists");
				}
#endif
			}
			
			_axesTable.Clear();
			foreach(InputConfiguration inputConfig in inputConfigurations)
			{
				Dictionary<string, AxisConfiguration> table = new Dictionary<string, AxisConfiguration>();
				foreach(AxisConfiguration axisConfig in inputConfig.axes)
				{
					if(!table.ContainsKey(axisConfig.name))
					{
						table.Add(axisConfig.name, axisConfig);
					}
#if UNITY_EDITOR
					else
					{
						Debug.LogWarning(string.Format("Input configuration \'{0}\' already contains an axis named \'{1}\'", inputConfig.name, axisConfig.name));
					}
#endif
				}
				
				_axesTable.Add(inputConfig.name, table);
			}
		}
		
		private void Update()
		{
#if UNITY_EDITOR
			if(!UnityEditor.EditorApplication.isPlaying)
				return;
#endif
			if(_currentConfiguration != null)
			{
				for(int i = 0; i < _currentConfiguration.axes.Count; i++)
				{
					_currentConfiguration.axes[i].Update();
				}
				if(RemoteUpdate != null)
					RemoteUpdate();

				if(_scanFlags != ScanFlags.None)
					ScanInput();
			}
			else
			{
				if(_scanFlags != ScanFlags.None)
					StopInputScan();
			}
		}

		private void ScanInput()
		{
			float timeout = ignoreTimescale ? (Time.realtimeSinceStartup - _scanStartTime) : (Time.time - _scanStartTime);
			if(!string.IsNullOrEmpty(_cancelScanButton) && GetButtonDown(_cancelScanButton) || timeout >= _scanTimeout)
			{
				StopInputScan();
				return;
			}

			bool scanSuccess = false;
			if(((int)_scanFlags & (int)ScanFlags.Key) == (int)ScanFlags.Key)
			{
				scanSuccess = ScanKey();
			}
			if(!scanSuccess && (((int)_scanFlags & (int)ScanFlags.JoystickButton) == (int)ScanFlags.JoystickButton))
			{
				scanSuccess = ScanJoystickButton();
			}
			if(!scanSuccess && (((int)_scanFlags & (int)ScanFlags.JoystickAxis) == (int)ScanFlags.JoystickAxis))
			{
				scanSuccess = ScanJoystickAxis();
			}
			if(!scanSuccess && (((int)_scanFlags & (int)ScanFlags.MouseAxis) == (int)ScanFlags.MouseAxis))
			{
				ScanMouseAxis();
			}
		}

		private bool ScanKey()
		{
			for(int i = 0; i < _keys.Length; i++)
			{
				if((int)_keys[i] >= (int)KeyCode.JoystickButton0)
					break;

				if(Input.GetKeyDown(_keys[i]))
				{
					_scanResult.scanFlags = ScanFlags.Key;
					_scanResult.key = _keys[i];
					_scanResult.joystick = -1;
					_scanResult.joystickAxis = -1;
					_scanResult.mouseAxis = -1;
					_scanResult.userData = _scanUserData;
					if(_scanHandler(_scanResult))
					{
						_scanHandler = null;
						_scanResult.userData = null;
						_scanFlags = ScanFlags.None;
						return true;
					}
				}
			}

			return false;
		}

		private bool ScanJoystickButton()
		{
			for(int key = (int)KeyCode.JoystickButton0; key < (int)KeyCode.Joystick4Button19; key++)
			{
				if(Input.GetKeyDown((KeyCode)key))
				{
					_scanResult.scanFlags = ScanFlags.JoystickButton;
					_scanResult.key = (KeyCode)key;
					_scanResult.joystick = -1;
					_scanResult.joystickAxis = -1;
					_scanResult.mouseAxis = -1;
					_scanResult.userData = _scanUserData;
					if(_scanHandler(_scanResult))
					{
						_scanHandler = null;
						_scanResult.userData = null;
						_scanFlags = ScanFlags.None;
						return true;
					}
				}
			}

			return false;
		}

		private bool ScanJoystickAxis()
		{
			int scanStart = _scanJoystick * AxisConfiguration.MaxJoystickAxes;
			for(int i = 0; i < AxisConfiguration.MaxJoystickAxes; i++)
			{
				if(Mathf.Abs(Input.GetAxisRaw(_rawJoystickAxes[scanStart + i])) >= 1.0f)
				{
					_scanResult.scanFlags = ScanFlags.JoystickAxis;
					_scanResult.key = KeyCode.None;
					_scanResult.joystick = _scanJoystick;
					_scanResult.joystickAxis = i;
					_scanResult.mouseAxis = -1;
					_scanResult.userData = _scanUserData;
					if(_scanHandler(_scanResult))
					{
						_scanHandler = null;
						_scanResult.userData = null;
						_scanFlags = ScanFlags.None;
						return true;
					}
				}
			}

			return false;
		}

		private bool ScanMouseAxis()
		{
			for(int i = 0; i < _rawMouseAxes.Length; i++)
			{
				if(Mathf.Abs(Input.GetAxis(_rawMouseAxes[i])) > 0.0f)
				{
					_scanResult.scanFlags = ScanFlags.MouseAxis;
					_scanResult.key = KeyCode.None;
					_scanResult.joystick = -1;
					_scanResult.joystickAxis = -1;
					_scanResult.mouseAxis = i;
					_scanResult.userData = _scanUserData;
					if(_scanHandler(_scanResult))
					{
						_scanHandler = null;
						_scanResult.userData = null;
						_scanFlags = ScanFlags.None;
						return true;
					}
				}
			}

			return false;
		}

		private void StopInputScan()
		{
			_scanResult.scanFlags = ScanFlags.None;
			_scanResult.key = KeyCode.None;
			_scanResult.joystick = -1;
			_scanResult.joystickAxis = -1;
			_scanResult.mouseAxis = -1;
			_scanResult.userData = _scanUserData;

			_scanHandler(_scanResult);

			_scanHandler = null;
			_scanResult.userData = null;
			_scanFlags = ScanFlags.None;
		}
		
		private void RaiseConfigurationChangedEvent(string configuration)
		{
			if(ConfigurationChanged != null)
			{
				ConfigurationChanged(configuration);
			}
		}

		private void RaiseConfigurationDirtyEvent(InputConfiguration configuration)
		{
			if(ConfigurationDirty != null)
				ConfigurationDirty(configuration);
		}
		
		private void RaiseLoadedEvent()
		{
			if(Loaded != null)
			{
				Loaded();
			}
		}
		
		private void RaiseSavedEvent()
		{
			if(Saved != null)
			{
				Saved();
			}
		}
		
		#region [Static Interface]
		public static InputManager Instance
		{
			get
			{
				return _instance;
			}
		}

		public static bool IgnoreTimescale
		{
			get
			{
				return _instance.ignoreTimescale;
			}
		}

		public static bool IsScanning
		{
			get
			{
				return _instance._scanFlags != ScanFlags.None;
			}
		}
		
		public static InputConfiguration CurrentConfiguration
		{
			get
			{
				return _instance._currentConfiguration;
			}
		}
		
		public static List<InputConfiguration> InputConfigurations
		{
			get
			{
				return _instance.inputConfigurations;
			}
		}
		
		public static bool AnyInput()
		{
			return AnyInput(_instance._currentConfiguration);
		}
		
		public static bool AnyInput(string configuration)
		{
			InputConfiguration inputConfig;
			if(_instance._configurationTable.TryGetValue(configuration, out inputConfig))
			{
				return AnyInput(inputConfig);
			}
			
			return false;
		}
		
		private static bool AnyInput(InputConfiguration inputConfig)
		{
			for(int i = 0; i < inputConfig.axes.Count; i++)
			{
				if(inputConfig.axes[i].AnyInput)
					return true;
			}
			
			return false;
		}

		/// <summary>
		/// If an axis with the requested name exists, and it is of type 'RemoteAxis', the axis' value will be changed.
		/// </summary>
		public static void SetRemoteAxisValue(string axisName, float value)
		{
			SetRemoteAxisValue(_instance._currentConfiguration.name, axisName, value);
		}

		/// <summary>
		/// If an axis with the requested name exists, and it is of type 'RemoteAxis', the axis' value will be changed.
		/// </summary>
		public static void SetRemoteAxisValue(InputConfiguration inputConfig, string axisName, float value)
		{
			SetRemoteAxisValue(inputConfig.name, axisName, value);
		}

		/// <summary>
		/// If an axis with the requested name exists, and it is of type 'RemoteAxis', the axis' value will be changed.
		/// </summary>
		public static void SetRemoteAxisValue(string inputConfigName, string axisName, float value)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(inputConfigName, axisName);
			if(axisConfig == null) 
				throw new ArgumentException(string.Format("An axis named \'{0}\' does not exist in the input configuration named \'{1}\'", axisName, inputConfigName));

			axisConfig.SetRemoteAxisValue(value);
		}

		/// <summary>
		/// If an button with the requested name exists, and it is of type 'RemoteButton', the button's state will be changed.
		/// </summary>
		public static void SetRemoteButtonValue(string buttonName, bool down, bool justChanged)
		{
			SetRemoteButtonValue(_instance._currentConfiguration.name, buttonName, down, justChanged);
		}
		
		/// <summary>
		/// If an button with the requested name exists, and it is of type 'RemoteButton', the button's state will be changed.
		/// </summary>
		public static void SetRemoteButtonValue(InputConfiguration inputConfig, string buttonName, bool down, bool justChanged)
		{
			SetRemoteButtonValue(inputConfig.name, buttonName, down, justChanged);
		}

		/// <summary>
		/// If an button with the requested name exists, and it is of type 'RemoteButton', the button's state will be changed.
		/// </summary>
		private static void SetRemoteButtonValue(string inputConfigName, string buttonName, bool down, bool justChanged)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(inputConfigName, buttonName);
			if(axisConfig == null) 
				throw new ArgumentException(string.Format("An button named \'{0}\' does not exist in the input configuration named \'{1}\'", buttonName, inputConfigName));

			axisConfig.SetRemoteButtonValue(down, justChanged);
		}
		
		/// <summary>
		/// Resets the internal state of the InputManager.
		/// </summary>
		public static void Reinitialize()
		{
			_instance.Initialize();
		}
		
		public static InputConfiguration CreateInputConfiguration(string name)
		{
			if(_instance._configurationTable.ContainsKey(name))
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' already exists", name));
			
			InputConfiguration inputConfig = new InputConfiguration(name);
			_instance.inputConfigurations.Add(inputConfig);
			_instance._configurationTable.Add(name, inputConfig);
			_instance._axesTable.Add(name, new Dictionary<string, AxisConfiguration>());
			
			return inputConfig;
		}
		
		public static bool DeleteInputConfiguration(string name)
		{
			InputConfiguration inputConfig = GetConfiguration(name);
			if(inputConfig == null)
				return false;
			
			_instance._axesTable.Remove(name);
			_instance._configurationTable.Remove(name);
			_instance.inputConfigurations.Remove(inputConfig);
			if(_instance._currentConfiguration.name == inputConfig.name)
			{
				if(_instance.inputConfigurations.Count == 0)
				{
					_instance._currentConfiguration = null;
				}
				else if(string.IsNullOrEmpty(_instance.defaultConfiguration) || 
						!_instance._configurationTable.ContainsKey(_instance.defaultConfiguration))
				{
					_instance._currentConfiguration = _instance.inputConfigurations[0];
				}
				else
				{
					_instance._currentConfiguration = _instance._configurationTable[_instance.defaultConfiguration];
				}
			}
			
			return true;
		}
		
		public static void SetConfiguration(string name)
		{
			if(_instance._currentConfiguration != null && name == _instance._currentConfiguration.name)
				return;
			
			if(!_instance._configurationTable.ContainsKey(name)) {
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", name));
			}
			
			_instance._currentConfiguration = _instance._configurationTable[name];
			ResetInputAxes();
			_instance.RaiseConfigurationChangedEvent(name);
		}
		
		public static InputConfiguration GetConfiguration(string name)
		{
			InputConfiguration inputConfig;
			if(_instance._configurationTable.TryGetValue(name, out inputConfig))
			{
				return inputConfig;
			}
			
			return null;
		}
		
		/// <summary>
		/// Gets a list of all input configuration names. 
		/// A new array is created every time you call this method.
		/// </summary>
		public static string[] GetConfigurationNames()
		{
			string[] names = new string[_instance.inputConfigurations.Count];
			for(int i = 0; i < names.Length; i++)
			{
				names[i] = _instance.inputConfigurations[i].name;
			}
			
			return names;
		}
		
		public static AxisConfiguration CreateButton(string configuration, string name, KeyCode key)
		{
			return CreateButton(configuration, name, key, KeyCode.None);
		}
		
		public static AxisConfiguration CreateButton(string configuration, string name, KeyCode primaryKey, KeyCode secondaryKey)
		{
			InputConfiguration inputConfig = GetConfiguration(configuration);
			if(inputConfig == null)
			{
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", configuration));
			}
			if(_instance._axesTable[configuration].ContainsKey(name))
			{
				string error = string.Format("The input configuration named {0} already contains an axis configuration named {1}", configuration, name);
				throw new ArgumentException(error);
			}
			
			AxisConfiguration axisConfig = new AxisConfiguration(name);
			axisConfig.type = InputType.Button;
			axisConfig.positive = primaryKey;
			axisConfig.altPositive = secondaryKey;
			axisConfig.Initialize();
			inputConfig.axes.Add(axisConfig);
			
			var table = _instance._axesTable[configuration];
			table.Add(name, axisConfig);
			
			return axisConfig;
		}
		
		public static AxisConfiguration CreateDigitalAxis(string configuration, string name, KeyCode positive, 
														  KeyCode negative, float gravity, float sensitivity)
		{
			return CreateDigitalAxis(configuration, name, positive, negative, KeyCode.None, KeyCode.None, gravity, sensitivity);
		}
		
		public static AxisConfiguration CreateDigitalAxis(string configuration, string name, KeyCode positive, KeyCode negative, 
														  KeyCode altPositive, KeyCode altNegative, float gravity, float sensitivity)
		{
			InputConfiguration inputConfig = GetConfiguration(configuration);
			if(inputConfig == null)
			{
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", configuration));
			}
			if(_instance._axesTable[configuration].ContainsKey(name))
			{
				string error = string.Format("The input configuration named {0} already contains an axis configuration named {1}", configuration, name);
				throw new ArgumentException(error);
			}
			
			AxisConfiguration axisConfig = new AxisConfiguration(name);
			axisConfig.type = InputType.DigitalAxis;
			axisConfig.positive = positive;
			axisConfig.negative = negative;
			axisConfig.altPositive = altPositive;
			axisConfig.altNegative = altNegative;
			axisConfig.gravity = gravity;
			axisConfig.sensitivity = sensitivity;
			axisConfig.Initialize();
			inputConfig.axes.Add(axisConfig);
			
			var table = _instance._axesTable[configuration];
			table.Add(name, axisConfig);
			
			return axisConfig;
		}
		
		public static AxisConfiguration CreateMouseAxis(string configuration, string name, int axis, float sensitivity)
		{
			InputConfiguration inputConfig = GetConfiguration(configuration);
			if(inputConfig == null)
			{
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", configuration));
			}
			if(_instance._axesTable[configuration].ContainsKey(name))
			{
				string error = string.Format("The input configuration named {0} already contains an axis configuration named {1}", configuration, name);
				throw new ArgumentException(error);
			}
			if(axis < 0 || axis > 2)
				throw new ArgumentOutOfRangeException("axis");
			
			AxisConfiguration axisConfig = new AxisConfiguration(name);
			axisConfig.type = InputType.MouseAxis;
			axisConfig.axis = axis;
			axisConfig.sensitivity = sensitivity;
			axisConfig.Initialize();
			inputConfig.axes.Add(axisConfig);
			
			var table = _instance._axesTable[configuration];
			table.Add(name, axisConfig);
			
			return axisConfig;
		}
		
		public static AxisConfiguration CreateAnalogAxis(string configuration, string name, int joystick, int axis, float sensitivity, float deadZone)
		{
			InputConfiguration inputConfig = GetConfiguration(configuration);
			if(inputConfig == null)
			{
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", configuration));
			}
			if(_instance._axesTable[configuration].ContainsKey(name))
			{
				string error = string.Format("The input configuration named {0} already contains an axis configuration named {1}", configuration, name);
				throw new ArgumentException(error);
			}
			if(axis < 0 || axis > 9)
				throw new ArgumentOutOfRangeException("axis");
			if(joystick < 0 || joystick > 3)
				throw new ArgumentOutOfRangeException("joystick");
			
			AxisConfiguration axisConfig = new AxisConfiguration(name);
			axisConfig.type = InputType.AnalogAxis;
			axisConfig.axis = axis;
			axisConfig.joystick = joystick;
			axisConfig.deadZone = deadZone;
			axisConfig.sensitivity = sensitivity;
			axisConfig.Initialize();
			inputConfig.axes.Add(axisConfig);
			
			var table = _instance._axesTable[configuration];
			table.Add(name, axisConfig);
			
			return axisConfig;
		}
		
		public static AxisConfiguration CreateEmptyAxis(string configuration, string name)
		{
			InputConfiguration inputConfig = GetConfiguration(configuration);
			if(inputConfig == null)
			{
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", configuration));
			}
			if(_instance._axesTable[configuration].ContainsKey(name))
			{
				string error = string.Format("The input configuration named {0} already contains an axis configuration named {1}", configuration, name);
				throw new ArgumentException(error);
			}
			
			AxisConfiguration axisConfig = new AxisConfiguration(name);
			axisConfig.Initialize();
			inputConfig.axes.Add(axisConfig);
			
			var table = _instance._axesTable[configuration];
			table.Add(name, axisConfig);
			
			return axisConfig;
		}
		
		public static bool DeleteAxisConfiguration(string configuration, string axisName)
		{
			InputConfiguration inputConfig = GetConfiguration(configuration);
			AxisConfiguration axisConfig = GetAxisConfiguration(configuration, axisName);
			if(inputConfig != null && axisConfig != null)
			{
				_instance._axesTable[inputConfig.name].Remove(axisConfig.name);
				inputConfig.axes.Remove(axisConfig);
				return true;
			}
			
			return false;
		}
		
		public static AxisConfiguration GetAxisConfiguration(string axisName)
		{
			if(_instance._currentConfiguration == null)
				throw new NullReferenceException("The input manager has no active input configuration");
			
			return GetAxisConfiguration(_instance._currentConfiguration.name, axisName);
		}
		
		public static AxisConfiguration GetAxisConfiguration(string configuration, string axisName)
		{
			Dictionary<string, AxisConfiguration> table;
			if(_instance._axesTable.TryGetValue(configuration, out table))
			{
				AxisConfiguration axisConfig;
				if(table.TryGetValue(axisName, out axisConfig))
					return axisConfig;
			}
			
			return null;
		}
		
		/// <summary>
		/// Scans for keyboard input and calls the handler with the result.
		/// Returns KeyCode.None if timeout is reached.
		/// </summary>
		public static void StartKeyScan(KeyScanHandler scanHandler, float timeout, string cancelScanButton, params object[] userData)
		{
			_instance._scanTimeout = timeout;
			_instance._scanFlags = ScanFlags.Key | ScanFlags.JoystickButton;
			_instance._scanStartTime = _instance.ignoreTimescale ? Time.realtimeSinceStartup : Time.time;
			_instance._cancelScanButton = cancelScanButton;
			_instance._scanUserData = userData;
			_instance._scanHandler = (result) => {
				if(result.scanFlags == ScanFlags.Key || result.scanFlags == ScanFlags.JoystickButton)
					return scanHandler(result.key, (object[])result.userData);
				else
					return scanHandler(KeyCode.None, (object[])result.userData);
			};
		}
		
		
		/// <summary>
		/// Scans for mouse input and calls the handler with the result.
		/// Returns -1 if timeout is reached.
		/// </summary>
		public static void StartMouseAxisScan(AxisScanHandler scanHandler, float timeout, string cancelScanButton, params object[] userData)
		{
			_instance._scanTimeout = timeout;
			_instance._scanFlags = ScanFlags.MouseAxis;
			_instance._scanStartTime = _instance.ignoreTimescale ? Time.realtimeSinceStartup : Time.time;
			_instance._cancelScanButton = cancelScanButton;
			_instance._scanUserData = userData;
			_instance._scanHandler = (result) => { 
				return scanHandler(result.mouseAxis, (object[])result.userData); 
			};
		}
		
		/// <summary>
		/// Scans for joystick input and calls the handler with the result.
		/// Returns -1 if timeout is reached.
		/// </summary>
		public static void StartJoystickAxisScan(AxisScanHandler scanHandler, int joystick, float timeout, string cancelScanButton, params object[] userData)
		{
			if(joystick < 0 || joystick >= AxisConfiguration.MaxJoystickAxes)
			{
				throw new ArgumentOutOfRangeException("joystick");
			}
			
			_instance._scanTimeout = timeout;
			_instance._scanFlags = ScanFlags.JoystickAxis;
			_instance._scanStartTime = _instance.ignoreTimescale ? Time.realtimeSinceStartup : Time.time;
			_instance._cancelScanButton = cancelScanButton;
			_instance._scanJoystick = joystick;
			_instance._scanUserData = userData;
			_instance._scanHandler = (result) => { 
				return scanHandler(result.mouseAxis, (object[])result.userData); 
			};
		}

		public static void StartScan(ScanSettings settings, ScanHandler scanHandler)
		{
			if(settings.joystick < 0 || settings.joystick >= AxisConfiguration.MaxJoystickAxes)
				throw new ArgumentOutOfRangeException("joystick");
			
			_instance._scanTimeout = settings.timeout;
			_instance._scanFlags = settings.scanFlags;
			_instance._scanStartTime = _instance.ignoreTimescale ? Time.realtimeSinceStartup : Time.time;
			_instance._cancelScanButton = settings.cancelScanButton;
			_instance._scanJoystick = settings.joystick;
			_instance._scanUserData = settings.userData;
			_instance._scanHandler = scanHandler;
		}
		
		public static void CancelScan()
		{
			if(_instance._scanFlags != ScanFlags.None)
				_instance.StopInputScan();
		}

		public static void SetConfigurationDirty(string inputConfigName)
		{
			InputConfiguration inputConfig = GetConfiguration(inputConfigName);
			if(inputConfig == null)
				throw new ArgumentException(string.Format("An input configuration with the name \'{0}\' does not exist", inputConfigName));

			_instance.RaiseConfigurationDirtyEvent(inputConfig);
		}

		public static void SetConfigurationDirty(InputConfiguration inputConfig)
		{
			if(inputConfig == null)
				throw new ArgumentNullException("inputConfig");

			_instance.RaiseConfigurationDirtyEvent(inputConfig);
		}
		
		/// <summary>
		/// Saves the input configurations in the XML format, in Application.persistentDataPath.
		/// </summary>
		public static void Save()
		{
#if UNITY_WINRT && !UNITY_EDITOR
			string filename = Application.persistentDataPath + "/input_config.xml";
#else
			string filename = System.IO.Path.Combine(Application.persistentDataPath, "input_config.xml");
#endif
			Save(new InputSaverXML(filename));
		}
		
		/// <summary>
		/// Saves the input configurations in the XML format, at the specified location.
		/// </summary>
		public static void Save(string filename)
		{
			Save(new InputSaverXML(filename));
		}
		
		public static void Save(IInputSaver inputSaver)
		{
			if(inputSaver == null)
				throw new ArgumentNullException("inputSaver");
			
			inputSaver.Save(_instance.inputConfigurations, _instance.defaultConfiguration);
			_instance.RaiseSavedEvent();
		}
		
		/// <summary>
		/// Loads the input configurations saved in the XML format, from Application.persistentDataPath.
		/// </summary>
		public static void Load()
		{
#if UNITY_WINRT && !UNITY_EDITOR
			string filename = Application.persistentDataPath + "/input_config.xml";
			if(UnityEngine.Windows.File.Exists(filename))
			{
				Load(new InputLoaderXML(filename));
			}
#else
			string filename = System.IO.Path.Combine(Application.persistentDataPath, "input_config.xml");
			if(System.IO.File.Exists(filename))
			{
				Load(new InputLoaderXML(filename));
			}
#endif
		}
		
		/// <summary>
		/// Loads the input configurations saved in the XML format, from the specified location.
		/// </summary>
		public static void Load(string filename)
		{
			Load(new InputLoaderXML(filename));
		}
		
		public static void Load(IInputLoader inputLoader)
		{
			if(inputLoader == null)
				throw new ArgumentNullException("inputLoader");
			
			inputLoader.Load(out _instance.inputConfigurations, out _instance.defaultConfiguration);
			_instance.Initialize();
			_instance.RaiseLoadedEvent();
		}
		
		#region [UNITY Interface]
		public static Vector3 acceleration
		{
			get
			{
				return Input.acceleration;
			}
		}
		
		public static int accelerationEventCount
		{
			get
			{
				return Input.accelerationEventCount;
			}
		}
		
		public static AccelerationEvent[] accelerationEvents
		{
			get
			{
				return Input.accelerationEvents;
			}
		}
		
		public static bool anyKey
		{
			get
			{
				return Input.anyKey;
			}
		}
		
		public static bool anyKeyDown
		{
			get
			{
				return Input.anyKeyDown;
			}
		}
		
		public static Compass compass
		{
			get
			{
				return Input.compass;
			}
		}
		
		public static bool compensateSensors
		{
			get
			{
				return Input.compensateSensors;
			}
			set
			{
				Input.compensateSensors = value;
			}
		}
		
		public static Vector2 compositionCursorPos
		{
			get
			{
				return Input.compositionCursorPos;
			}
			set
			{
				Input.compositionCursorPos = value;
			}
		}
		
		public static string compositionString
		{
			get
			{
				return Input.compositionString;
			}
		}
		
		public static DeviceOrientation deviceOrientation
		{
			get
			{
				return Input.deviceOrientation;
			}
		}
		
		public static Gyroscope gyro
		{
			get
			{
				return Input.gyro;
			}
		}
		
		public static IMECompositionMode imeCompositionMode
		{
			get
			{
				return Input.imeCompositionMode;
			}
			set
			{
				Input.imeCompositionMode = value;
			}
		}
		
		public static bool imeIsSelected
		{
			get
			{
				return Input.imeIsSelected;
			}
		}
		
		public static string inputString
		{
			get
			{
				return Input.inputString;
			}
		}
		
		public static LocationService location
		{
			get
			{
				return Input.location;
			}
		}
		
		public static Vector2 mousePosition
		{
			get
			{
				return Input.mousePosition;
			}
		}

		public static bool mousePresent
		{
			get
			{
				return Input.mousePresent;
			}
		}
		
		public static bool multiTouchEnabled
		{
			get
			{
				return Input.multiTouchEnabled;
			}
			set
			{
				Input.multiTouchEnabled = value;
			}
		}
		
		public static int touchCount
		{
			get
			{
				return Input.touchCount;
			}
		}
		
		public static Touch[] touches
		{
			get
			{
				return Input.touches;
			}
		}
		
		public static AccelerationEvent GetAccelerationEvent(int index)
		{
			return Input.GetAccelerationEvent(index);
		}
		
		public static float GetAxis(string name)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(name);
			if(axisConfig == null) 
			{
				string error = string.Format("An axis named \'{0}\' does not exist in the active input configuration", name);
				throw new ArgumentException(error);
			}
			
			return axisConfig.GetAxis();
		}
		
		public static float GetAxisRaw(string name)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(name);
			if(axisConfig == null) 
			{
				string error = string.Format("An axis named \'{0}\' does not exist in the active input configuration", name);
				throw new ArgumentException(error);
			}
			
			return axisConfig.GetAxisRaw();
		}
		
		public static bool GetButton(string name)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(name);
			if(axisConfig == null) 
			{
				string error = string.Format("An axis named \'{0}\' does not exist in the active input configuration", name);
				throw new ArgumentException(error);
			}
			
			return axisConfig.GetButton();
		}
		
		public static bool GetButtonDown(string name)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(name);
			if(axisConfig == null) 
			{
				string error = string.Format("An axis named \'{0}\' does not exist in the active input configuration", name);
				throw new ArgumentException(error);
			}
			
			return axisConfig.GetButtonDown();
		}
		
		public static bool GetButtonUp(string name)
		{
			AxisConfiguration axisConfig = GetAxisConfiguration(name);
			if(axisConfig == null) 
			{
				string error = string.Format("An axis named \'{0}\' does not exist in the active input configuration", name);
				throw new ArgumentException(error);
			}
			
			return axisConfig.GetButtonUp();
		}
		
		public static bool GetKey(string name)
		{
			KeyCode key;
			if(_instance._stringToKeyTable.TryGetValue(name, out key))
			{
				return Input.GetKey(key);
			}
			else
			{
				return Input.GetKey(name);
			}
		}
		
		public static bool GetKey(KeyCode key)
		{
			return Input.GetKey(key);
		}
		
		public static bool GetKeyDown(string name)
		{
			KeyCode key;
			if(_instance._stringToKeyTable.TryGetValue(name, out key))
			{
				return Input.GetKeyDown(key);
			}
			else
			{
				return Input.GetKeyDown(name);
			}
		}
		
		public static bool GetKeyDown(KeyCode key)
		{
			return Input.GetKeyDown(key);
		}
		
		public static bool GetKeyUp(string name)
		{
			KeyCode key;
			if(_instance._stringToKeyTable.TryGetValue(name, out key))
			{
				return Input.GetKeyUp(key);
			}
			else
			{
				return Input.GetKeyUp(name);
			}
		}
		
		public static bool GetKeyUp(KeyCode key)
		{
			return Input.GetKeyUp(key);
		}
		
		public static bool GetMouseButton(int index)
		{
			return Input.GetMouseButton(index);
		}
		
		public static bool GetMouseButtonDown(int index)
		{
			return Input.GetMouseButtonDown(index);
		}
		
		public static bool GetMouseButtonUp(int index)
		{
			return Input.GetMouseButtonUp(index);
		}
		
		public static Touch GetTouch(int index)
		{
			return Input.GetTouch(index);
		}
		
		public static string[] GetJoystickNames()
		{
			return Input.GetJoystickNames();
		}
		
		public static void ResetInputAxes()
		{
			InputConfiguration inputConfig = _instance._currentConfiguration;
			for(int i = 0; i < inputConfig.axes.Count; i++)
			{
				inputConfig.axes[i].Reset();
			}
			Input.ResetInputAxes();
		}
		#endregion
		
		#endregion
	}
}