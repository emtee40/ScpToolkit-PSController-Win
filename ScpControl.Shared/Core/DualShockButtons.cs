﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace ScpControl.Shared.Core
{
    #region Interfaces

    /// <summary>
    ///     Describes the possible states for a DualShock button.
    /// </summary>
    public interface IDsButtonState
    {
        bool IsPressed { get; set; }
        float Pressure { get; }
        byte Value { get; }
    }

    /// <summary>
    ///     Describes a DualShock button.
    /// </summary>
    public interface IDsButton
    {
        uint Offset { get; }
        string Name { get; }
        string DisplayName { get; }
        int MaskOffset { get; }
        int ArrayIndex { get; }
        void ToggleBit(ref byte source, bool value);
    }

    #endregion

    /// <summary>
    ///     Implements the possible states for a DualShock button.
    /// </summary>
    public class DsButtonState : IDsButtonState
    {
        #region Properties

        /// <summary>
        ///     True if the button in question is currently pressed, false if it's released.
        /// </summary>
        public bool IsPressed { get; set; }

        /// <summary>
        ///     Gets the pressure value of the current button compatible with PCSX2s XInput/LilyPad mod.
        /// </summary>
        /// <remarks>This is just a boolean to float conversion.</remarks>
        public float Pressure
        {
            get { return IsPressed ? 1.0f : 0.0f; }
        }

        /// <summary>
        ///     Gets the button press state as byte value.
        /// </summary>
        /// <remarks>255 equals pressed, 0 equals released.</remarks>
        public byte Value
        {
            get { return (byte) (IsPressed ? 0xFF : 0x00); }
        }

        #endregion
    }

    /// <summary>
    ///     Implements a DualShock button.
    /// </summary>
    [DataContract]
    public class DsButton : IDsButton
    {
        #region Ctors

        public DsButton()
        {
        }

        public DsButton(string name)
            : this()
        {
            Name = name;
        }

        #endregion

        #region Properties

        [DataMember]
        public uint Offset { get; protected set; }

        /// <summary>
        ///     The short name identifying the button.
        /// </summary>
        [DataMember]
        public string Name { get; private set; }

        /// <summary>
        ///     A short descriptive name of the button.
        /// </summary>
        [DataMember]
        public string DisplayName { get; protected set; }

        /// <summary>
        ///     The bit offset within the <see cref="ArrayIndex" />
        /// </summary>
        [DataMember]
        public int MaskOffset { get; protected set; }

        /// <summary>
        ///     The corresponding byte in the <see cref="ScpHidReport.RawBytes" /> holding the value of the button.
        /// </summary>
        [DataMember]
        public int ArrayIndex { get; protected set; }

        #endregion

        #region Methods

        /// <summary>
        ///     Sets or unsets a given bit in a specified byte.
        /// </summary>
        /// <param name="source">The byte to manipulate.</param>
        /// <param name="value">True if the bit should be set high, false otherwise.</param>
        /// <remarks>If the bit is already high it will be overwritten and vice versa.</remarks>
        public void ToggleBit(ref byte source, bool value)
        {
            if (value)
            {
                source |= (byte) (1 << MaskOffset);
            }
            else
            {
                source &= (byte) ~(1 << MaskOffset);
            }
        }

        public override bool Equals(object obj)
        {
            var button = obj as DsButton;

            return button != null && button.Name.Equals(Name);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public override string ToString()
        {
            return DisplayName;
        }

        #endregion
    }

    /// <summary>
    ///     Definition of a DualShock 3 button.
    /// </summary>
    public class Ds3Button : DsButton
    {
        #region Properties

        private static readonly Lazy<IEnumerable<Ds3Button>> Ds3Buttons =
            new Lazy<IEnumerable<Ds3Button>>(() => typeof (Ds3Button).GetProperties(
                BindingFlags.Public | BindingFlags.Static)
                .Select(b => b.GetValue(null, null))
                .Where(o => o.GetType() == typeof (Ds3Button)).Cast<Ds3Button>());

        public static IEnumerable<Ds3Button> Buttons
        {
            get { return Ds3Buttons.Value; }
        }

        #endregion

        #region Ctors

        public Ds3Button()
        {
        }

        public Ds3Button(string name)
            : base(name)
        {
        }

        #endregion

        #region Buttons

        private static readonly Lazy<IDsButton> DsBtnNone = new Lazy<IDsButton>(() => new Ds3Button("None")
        {
            Offset = 0,
            DisplayName = "None"
        });

        public static IDsButton None
        {
            get { return DsBtnNone.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnSelect = new Lazy<IDsButton>(() => new Ds3Button("Select")
        {
            Offset = 1 << 0,
            DisplayName = "Select",
            ArrayIndex = 10,
            MaskOffset = 0
        });

        public static IDsButton Select
        {
            get { return DsBtnSelect.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnL3 = new Lazy<IDsButton>(() => new Ds3Button("L3")
        {
            Offset = 1 << 1,
            DisplayName = "Left thumb",
            ArrayIndex = 10,
            MaskOffset = 1
        });

        public static IDsButton L3
        {
            get { return DsBtnL3.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnR3 = new Lazy<IDsButton>(() => new Ds3Button("R3")
        {
            Offset = 1 << 2,
            DisplayName = "Right thumb",
            ArrayIndex = 10,
            MaskOffset = 2
        });

        public static IDsButton R3
        {
            get { return DsBtnR3.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnStart = new Lazy<IDsButton>(() => new Ds3Button("Start")
        {
            Offset = 1 << 3,
            DisplayName = "Start",
            ArrayIndex = 10,
            MaskOffset = 3
        });

        public static IDsButton Start
        {
            get { return DsBtnStart.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnUp = new Lazy<IDsButton>(() => new Ds3Button("Up")
        {
            Offset = 1 << 4,
            DisplayName = "D-Pad up",
            ArrayIndex = 10,
            MaskOffset = 4
        });

        public static IDsButton Up
        {
            get { return DsBtnUp.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnRight = new Lazy<IDsButton>(() => new Ds3Button("Right")
        {
            Offset = 1 << 5,
            DisplayName = "D-Pad right",
            ArrayIndex = 10,
            MaskOffset = 5
        });

        public static IDsButton Right
        {
            get { return DsBtnRight.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnDown = new Lazy<IDsButton>(() => new Ds3Button("Down")
        {
            Offset = 1 << 6,
            DisplayName = "D-Pad down",
            ArrayIndex = 10,
            MaskOffset = 6
        });

        public static IDsButton Down
        {
            get { return DsBtnDown.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnLeft = new Lazy<IDsButton>(() => new Ds3Button("Left")
        {
            Offset = 1 << 7,
            DisplayName = "D-Pad left",
            ArrayIndex = 10,
            MaskOffset = 7
        });

        public static IDsButton Left
        {
            get { return DsBtnLeft.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnL2 = new Lazy<IDsButton>(() => new Ds3Button("L2")
        {
            Offset = 1 << 8,
            DisplayName = "Left trigger",
            ArrayIndex = 11,
            MaskOffset = 0
        });

        public static IDsButton L2
        {
            get { return DsBtnL2.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnR2 = new Lazy<IDsButton>(() => new Ds3Button("R2")
        {
            Offset = 1 << 9,
            DisplayName = "Right trigger",
            ArrayIndex = 11,
            MaskOffset = 1
        });

        public static IDsButton R2
        {
            get { return DsBtnR2.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnL1 = new Lazy<IDsButton>(() => new Ds3Button("L1")
        {
            Offset = 1 << 10,
            DisplayName = "Left shoulder",
            ArrayIndex = 11,
            MaskOffset = 2
        });

        public static IDsButton L1
        {
            get { return DsBtnL1.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnR1 = new Lazy<IDsButton>(() => new Ds3Button("R1")
        {
            Offset = 1 << 11,
            DisplayName = "Right shoulder",
            ArrayIndex = 11,
            MaskOffset = 3
        });

        public static IDsButton R1
        {
            get { return DsBtnR1.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnTriangle = new Lazy<IDsButton>(() => new Ds3Button("Triangle")
        {
            Offset = 1 << 12,
            DisplayName = "Triangle",
            ArrayIndex = 11,
            MaskOffset = 4
        });

        public static IDsButton Triangle
        {
            get { return DsBtnTriangle.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnCircle = new Lazy<IDsButton>(() => new Ds3Button("Circle")
        {
            Offset = 1 << 13,
            DisplayName = "Circle",
            ArrayIndex = 11,
            MaskOffset = 5
        });

        public static IDsButton Circle
        {
            get { return DsBtnCircle.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnCross = new Lazy<IDsButton>(() => new Ds3Button("Cross")
        {
            Offset = 1 << 14,
            DisplayName = "Cross",
            ArrayIndex = 11,
            MaskOffset = 6
        });

        public static IDsButton Cross
        {
            get { return DsBtnCross.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnSquare = new Lazy<IDsButton>(() => new Ds3Button("Square")
        {
            Offset = 1 << 15,
            DisplayName = "Square",
            ArrayIndex = 11,
            MaskOffset = 7
        });

        public static IDsButton Square
        {
            get { return DsBtnSquare.Value; }
        }

        private static readonly Lazy<IDsButton> DsBtnPs = new Lazy<IDsButton>(() => new Ds3Button("PS")
        {
            Offset = 1 << 16,
            DisplayName = "PS",
            ArrayIndex = 12,
            MaskOffset = 0
        });

        public static IDsButton Ps
        {
            get { return DsBtnPs.Value; }
        }

        #endregion
    }
}