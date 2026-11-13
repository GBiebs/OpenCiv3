using System.Collections.Generic;

namespace C7GameData.Save;

public class SaveInflow {
	public string name { get; set; }
	public int iconRowIndex = 0;
	public List<SaveLocalYield> localYield { get; set; }

	public SaveInflow() { }

	public SaveInflow(Inflow inflow) {
		this.name = inflow.name;
		this.iconRowIndex = inflow.iconRowIndex;
		this.localYield = inflow.localYield.ConvertAll(y => new SaveLocalYield(y.yieldType, y.yieldCalculationPath ?? GetYieldCalc(y.yieldType)));
	}

	public string GetYieldCalc(InflowYield yieldType) {
		return $"inflows.result.{this.name}.{yieldType}".ToLower();
	}
}
