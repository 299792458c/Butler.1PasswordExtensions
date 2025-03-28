using System;
using System.Text.Json.Serialization;

namespace Butler.OnePasswordExtensions;

public class ItemDetail
{
	[JsonPropertyName("additionalInformation")]
	public string AdditionalInformation { get; set; }
	
	[JsonPropertyName("category")]
	public string Category { get; set; }
	
	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }
	
	[JsonPropertyName("fields")]
	public Field[] Fields { get; set; }
	
	[JsonPropertyName("id")]
	public string Id { get; set; }
	
	[JsonPropertyName("lastEditedBy")]
	public string LastEditedBy { get; set; }
	
	[JsonPropertyName("tags")]
	public string[] Tags { get; set; }
	
	[JsonPropertyName("sections")]
	public Section[] Sections { get; set; }
	
	[JsonPropertyName("title")]
	public string Title { get; set; }
	
	[JsonPropertyName("updatedAt")]
	public DateTime UpdatedAt { get; set; }
	
	[JsonPropertyName("vault")]
	public Vault Vault { get; set; }
	
	[JsonPropertyName("version")]
	public int Version { get; set; }
	
	[JsonPropertyName("urls")]
	public Url[] Urls { get; set; }
}

public class Field
{
	[JsonPropertyName("id")]
	public string Id { get; set; }
	
	[JsonPropertyName("label")]
	public string Label { get; set; }
	
	[JsonPropertyName("purpose")]
	public string Purpose { get; set; }
	
	[JsonPropertyName("type")]
	public string Type { get; set; }
	
	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;
	
	[JsonPropertyName("section")]
	public Section Section { get; set; }
}

public class Section
{
	[JsonPropertyName("id")]
	public string Id { get; set; }
	
	[JsonPropertyName("label")]
	public string Label { get; set; }
}

public class Url
{
	[JsonPropertyName("href")]
	public string Href { get; set; }

	[JsonPropertyName("primary")]
	public bool Primary { get; set; }
}

public class Vault
{
	[JsonPropertyName("attributeVersion")]
	public int AttributeVersion { get; set; }
	
	[JsonPropertyName("contentVersion")]
	public int ContentVersion { get; set; }
	
	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }
	
	[JsonPropertyName("description")]
	public string Description { get; set; }
	
	[JsonPropertyName("id")]
	public string Id { get; set; }
	
	[JsonPropertyName("items")]
	public int Items { get; set; }
	
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("type")]
	public string Type { get; set; }
	
	[JsonPropertyName("updatedAt")]
	public DateTime UpdatedAt { get; set; }
}