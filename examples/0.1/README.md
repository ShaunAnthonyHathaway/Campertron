## Examples

ZionConfig.yaml

Searches against Watchman campground located in Zion National Park.  Results are refined to:

- Search 30 days from today
- Total of 2 Humans will be staying
- Show any day of the week
- Only include results with equipment attributes "Tent" "Small Tent" or "Large Tent over 9x12'"
- Only include results with site attributes "Shade:Yes" and Campfire Allowed:Yes"
- Exclude type attributes GROUP
- Allow results to show any consecutive days

SouthRimConfig.yaml

Searches against Mather campground located in Grand Canyon National Park at the south rim.  Results are refined to:

- Search 21 days from today
- Total of 2 Humans will be staying
- Show Friday and Saturday night availability only
- Allow results to show only results with 2 consecutive days

## General.yaml

outputTo:
- Type:Enum
- AllowedValue:Console
- AllowedValue:Email
- AllowedValue:HtmlFile
    
refreshRidbDataDayInterval:
- Type:Int

lastRidbDataRefresh:
- Type:Date

autoRefresh:
- Type:Boolean

## Email.yaml

smtpServer:
- Type:String

smtpPort:
- Type:Int

smtpUsername:
- Type:String

smtpPassword:
- Type:String

sendToAddressList:
- Type:Array(String)

sendFromAddress:
- Type:String

## Campsites.yaml (with any name)

displayName: 
- Type:String

autoRun: 
- Type:Boolean

campgroundID: 
- Type:Int

totalHumans: 
- Type:Int

searchBy: 
- Type:Enum
- AllowedValue:DaysOut
- AllowedValue:MonthsOut
- AllowedValue:SpecificDates
- AllowedValue:Until
- AllowedValue:StartEndDate

searchValue: 
- Type:Int

searchValueDates: 
- Type:Array(Dates)

showMonday: 
- Type:Boolean

showTuesday: 
- Type:Boolean

showWednesday: 
- Type:Boolean

showThursday: 
- Type:Boolean

showFriday: 
- Type:Boolean

showSaturday: 
- Type:Boolean

showSunday: 
- Type:Boolean

includeEquipment:
- Type:Array(String)

excludeEquipment: 
- Type:Array(String)

includeSites: 
- Type:Array(String)

excludeSites: 
- Type:Array(String)

includeAttributes:
- Type:Array(String)

excludeAttributes: 
- Type:Array(String)

includeCampsiteType: 
- Type:Array(String)

excludeCampsiteType:
- Type:Array(String)

consecutiveDays: 
- Type:Int
