// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Bicep.Local.Extension.Types.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class BicepStringMaxLengthAttribute : Attribute
{
    public int MaxLength { get; }

    public BicepStringMaxLengthAttribute(int maxLength)
    {
        MaxLength = maxLength;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class BicepStringMinLengthAttribute : Attribute
{
    public int MinLength { get; }

    public BicepStringMinLengthAttribute(int minLength)
    {
        MinLength = minLength;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class BicepStringPatternAttribute : Attribute
{
    public string Regex { get; }

    public BicepStringPatternAttribute(string regex)
    {
        Regex = regex;
    }
}
