using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace AssemblyAvalonia.Models.MetaData
{
	/// <summary>
	///     Base class for color data.
	/// </summary>
	public class ColorData : ValueField
	{
		private string _dataType;
		private bool _alpha;
		private bool _basic;
		private Color _value;

		public ColorData(string name, uint offset, long address, bool alpha, bool basic, string dataType, Color value,
			uint pluginLine, string tooltip)
			: base(name, offset, address, pluginLine, tooltip)
		{
			_value = value;
			_alpha = alpha;
			_basic = basic;
			_dataType = dataType;
		}

		public bool Alpha
		{
			get { return _alpha; }
			set
			{
				_alpha = value;
				NotifyPropertyChanged("Alpha");
			}
		}

		public bool Basic
		{
			get { return _basic; }
			set
			{
				_basic = value;
				NotifyPropertyChanged("Basic");
			}
		}

		public string DataType
		{
			get { return _dataType; }
			set
			{
				_dataType = value;
				NotifyPropertyChanged("DataType");
			}
		}

		public Color Value
		{
			get { return _value; }
			set
			{
				_value = value;
				NotifyPropertyChanged("Value");
			}
		}

		public static byte FloatToByte(float value)
		{
			float expand = value * 255f;

			if (expand > 255f)
				expand = 255f;
			else if (!(expand >= 0f && expand <= 255f))
				expand = 0f;

			return Convert.ToByte(expand);
		}

		public static float ByteToFloat(byte value)
		{
			//rounding doesnt work great for 128 so force it to 0.5
			return value == 128 ? 0.5f : value / 255f;
		}

		/// <summary>
		///     Converts an sRGB byte value to a linear (scRGB) float.
		/// </summary>
		public static float SrgbToLinear(byte value)
		{
			float s = value / 255f;
			return s <= 0.04045f
				? s / 12.92f
				: (float)Math.Pow((s + 0.055f) / 1.055f, 2.4);
		}

		/// <summary>
		///     Converts a linear (scRGB) float to an sRGB byte value.
		/// </summary>
		public static byte LinearToSrgb(float linear)
		{
			float s = linear <= 0.0031308f
				? 12.92f * linear
				: 1.055f * (float)Math.Pow(linear, 1.0 / 2.4) - 0.055f;
			return FloatToByte(s);
		}

		public override void Accept(IMetaFieldVisitor visitor)
		{
			if (DataType == "color32")
				visitor.VisitColourInt(this);
			else
				visitor.VisitColourFloat(this);
		}

		public override MetaField CloneValue()
		{
			return new ColorData(Name, Offset, FieldAddress, Alpha, Basic, DataType, Value, PluginLine, ToolTip);
		}

		public override string AsString()
		{
			if (DataType == "color32")
			{
				if (Alpha)
					return string.Format("{0} | {1} | {2} {3} {4} {5}", DataType, Name, Value.A, Value.R, Value.G, Value.B);

				return string.Format("{0} | {1} | {2} {3} {4}", DataType, Name, Value.R, Value.G, Value.B);
			}
			else if (Basic)
			{
				if (Alpha)
					return string.Format("{0} | {1} | {2} {3} {4} {5}", DataType, Name, ByteToFloat(Value.A), ByteToFloat(Value.R), ByteToFloat(Value.G), ByteToFloat(Value.B));

				return string.Format("{0} | {1} | {2} {3} {4}", DataType, Name, ByteToFloat(Value.R), ByteToFloat(Value.G), ByteToFloat(Value.B));
			}
			else
			{
				if (Alpha)
					return string.Format("{0} | {1} | {2} {3} {4} {5}", DataType, Name, ByteToFloat(Value.A), ByteToFloat(Value.R), ByteToFloat(Value.G), ByteToFloat(Value.B));

				return string.Format("{0} | {1} | {2} {3} {4}", DataType, Name, ByteToFloat(Value.R), ByteToFloat(Value.G), ByteToFloat(Value.B));
			}
		}

		public override object GetAsJson()
		{
			Dictionary<string, object> dict = new Dictionary<string, object>();
			if (DataType == "color32")
			{
				dict["Type"] = "integer";
				if (Alpha)
					dict["A"] = Value.A;
				dict["R"] = Value.R;
				dict["G"] = Value.G;
				dict["B"] = Value.B;
			}
			else if (Basic)
			{
				dict["Type"] = "float";
				if (Alpha)
					dict["A"] = ByteToFloat(Value.A);
				dict["R"] = ByteToFloat(Value.R);
				dict["G"] = ByteToFloat(Value.G);
				dict["B"] = ByteToFloat(Value.B);
			}
			else
			{
				dict["Type"] = "float";
				if (Alpha)
					dict["A"] = ByteToFloat(Value.A);
				dict["R"] = ByteToFloat(Value.R);
				dict["G"] = ByteToFloat(Value.G);
				dict["B"] = ByteToFloat(Value.B);
			}

			return dict;
		}
	}
}