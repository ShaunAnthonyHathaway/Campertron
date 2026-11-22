## Examples

ZionConfig.yaml

Searches against Watchman campground located in Zion National Park.  Results are refined to:

- Search 30 days from today
- Total of 2 Humans will be staying
- Show any day of the week
- Match the filters exactly (`filterMatching: Exact`)
- Only include campsites that allow at least one of the equipment types "Tent", "SMALL TENT" or "LARGE TENT OVER 9X12`"
- Only include campsites with at least one of the attributes "Shade:Yes" or "Campfire Allowed:Yes"
- Exclude campsites of the type "GROUP TENT ONLY AREA NONELECTRIC" (Watchman's group sites)
- Show single nights (`consecutiveDays: 1`)

SouthRimConfig.yaml

Searches against Mather campground located in Grand Canyon National Park at the south rim.  Results are refined to:

- Search 30 days from today
- Total of 2 Humans will be staying
- Show Friday and Saturday night availability only
- Allow results to show only results with 2 consecutive days

Ready-made settings for email, ntfy and both, and a Docker Compose file with read-only secrets: [../notifications](../notifications).

## General.yaml

outputTo:
- Type:Enum
- AllowedValue:Console
- AllowedValue:Email
- AllowedValue:HtmlFile
- AllowedValue:Ntfy (new in 0.2.0: ntfy notifications only, with Ntfy.yaml below)
    
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

smtpPasswordFile (optional, new in 0.2.0; 0.1.1 refuses a file that has it):
- Type:String, the absolute path of a file that holds the SMTP password; when it is set, smtpPassword is not used. Without it, the environment variable CAMPERTRON_EMAIL_PASSWORD is used if it is set, else smtpPassword.

sendToAddressList:
- Type:Array(String)

sendFromAddress:
- Type:String

## Ntfy.yaml (optional; new in 0.2.0)

ntfy push notifications (see the main README's "Notifications with ntfy"). With outputTo: Ntfy they are the only notifications; with outputTo: Email they are sent besides the emails while enabled is true. Version 0.1.1 and older take this file for a search file: move it out of the settings folder before running one of them.

enabled:
- Type:Boolean

server:
- Type:String, the ntfy server's address only, such as https://ntfy.sh (http or https, a host and an optional port; no path, topic, user name or password)

topic:
- Type:String, letters, digits, - and _, up to 64 characters; keep it private, like a password

tokenFile (optional):
- Type:String, the absolute path of a file that holds an ntfy access token; without it Campertron uses the environment variable CAMPERTRON_NTFY_TOKEN, else no token. There is no setting for the token itself.

priority (optional):
- Type:Enum
- AllowedValue:Min (1)
- AllowedValue:Low (2)
- AllowedValue:Default (3, the default)
- AllowedValue:High (4)
- AllowedValue:Max (5)

tags (optional):
- Type:Array(String), each 1 to 64 letters, digits, _, + or -; an emoji short code such as tent shows as the emoji. Without tags: tent

timeoutSeconds (optional):
- Type:Int, 1 to 120 (default 30)

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
- Write each date as yyyy-MM-dd, for example 2026-11-20. Campertron reads these dates with your computer's regional settings. In the settings that were checked and use the Gregorian calendar (US, UK, Australian, German, French, Mexican and Japanese), yyyy-MM-dd always means the same day, while 11/20/2026 is no date in the UK, Australian, German, French and Mexican settings and 11/5/2026 means 11 May there. Settings with another calendar, such as Thai Buddhist or Persian, read even yyyy-MM-dd differently.

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

filterMatching:
- Type:Enum
- AllowedValue:Exact
- AllowedValue:Contains
- How the eight include and exclude lists below match a campsite. Exact: a value must equal the campsite's value. Contains: the matching of searches written before 0.1.0-preview.2: an equipment or attribute value matches when it contains the campsite's equipment name or Name:Value (so RV/MOTORHOME matches a campsite that permits RV), a campsite type matches when it contains the value (so GROUP matches GROUP STANDARD NONELECTRIC). Campsite names (includeSites, excludeSites) always match exactly. Case, spaces and punctuation count in both. A search file without filterMatching uses Contains; a new search is written with Exact. Write it once, as Exact or Contains: anything else (an empty value, a number, other spellings) stops the search with an error.

The include and exclude lists (in the GUI's Searches tab, check lists of the chosen campground's own values): an include list keeps a campsite when any of its values matches; an exclude list leaves a campsite out when any of its values matches. An empty or missing list does not filter. (Before 0.1.0-preview.2, excludeEquipment and excludeAttributes left a campsite out only when every value matched, and includeCampsiteType kept one only when its type matched every value.)

includeEquipment:
- Type:Array(String)
- Equipment names as the campground data writes them, such as Tent or RV.

excludeEquipment: 
- Type:Array(String)

includeSites: 
- Type:Array(String)
- Campsite names, such as A012, always matched exactly.

excludeSites: 
- Type:Array(String)

includeAttributes:
- Type:Array(String)
- Attributes written Name:Value, such as Shade:Yes.

excludeAttributes: 
- Type:Array(String)

includeCampsiteType: 
- Type:Array(String)
- Campsite types, such as STANDARD NONELECTRIC.

excludeCampsiteType:
- Type:Array(String)

consecutiveDays: 
- Type:Int
