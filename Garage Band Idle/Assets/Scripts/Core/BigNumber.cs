using System;
using BreakInfinity;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Thin wrapper around BreakInfinity.BigDouble used for all currency and
    // production values. Game code depends on this type rather than the library,
    // so the backing implementation can be swapped without touching callers.
    [Serializable]
    public struct BigNumber : IComparable<BigNumber>, IEquatable<BigNumber>, IFormattable
    {
        [SerializeField] private BigDouble _value;

        public static readonly BigNumber Zero = new BigNumber(0);
        public static readonly BigNumber One = new BigNumber(1);

        public BigNumber(double value) { _value = value; }
        private BigNumber(BigDouble value) { _value = value; }

        // mantissa/exponent are exposed for display formatting (NumberFormatter);
        // game logic should stick to arithmetic and comparisons
        public double Mantissa => _value.Mantissa;
        public long Exponent => _value.Exponent;

        // only valid when the magnitude fits in a double; formatting guards the range
        public double ToDouble() => _value.ToDouble();

        public static implicit operator BigNumber(double value) => new BigNumber(value);
        public static implicit operator BigNumber(int value) => new BigNumber(value);
        public static implicit operator BigNumber(long value) => new BigNumber(value);

        public static BigNumber operator -(BigNumber value) => new BigNumber(-value._value);
        public static BigNumber operator +(BigNumber a, BigNumber b) => new BigNumber(a._value + b._value);
        public static BigNumber operator -(BigNumber a, BigNumber b) => new BigNumber(a._value - b._value);
        public static BigNumber operator *(BigNumber a, BigNumber b) => new BigNumber(a._value * b._value);
        public static BigNumber operator /(BigNumber a, BigNumber b) => new BigNumber(a._value / b._value);

        public static bool operator ==(BigNumber a, BigNumber b) => a._value == b._value;
        public static bool operator !=(BigNumber a, BigNumber b) => a._value != b._value;
        public static bool operator <(BigNumber a, BigNumber b) => a._value < b._value;
        public static bool operator <=(BigNumber a, BigNumber b) => a._value <= b._value;
        public static bool operator >(BigNumber a, BigNumber b) => a._value > b._value;
        public static bool operator >=(BigNumber a, BigNumber b) => a._value >= b._value;

        public static BigNumber Pow(BigNumber value, double power) => new BigNumber(BigDouble.Pow(value._value, power));
        public static BigNumber Max(BigNumber a, BigNumber b) => new BigNumber(BigDouble.Max(a._value, b._value));
        public static BigNumber Min(BigNumber a, BigNumber b) => new BigNumber(BigDouble.Min(a._value, b._value));
        public static BigNumber Floor(BigNumber value) => new BigNumber(BigDouble.Floor(value._value));

        public int CompareTo(BigNumber other) => _value.CompareTo(other._value);
        public bool Equals(BigNumber other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is BigNumber other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();

        public override string ToString() => _value.ToString();
        public string ToString(string format, IFormatProvider formatProvider) => _value.ToString(format, formatProvider);
    }
}
