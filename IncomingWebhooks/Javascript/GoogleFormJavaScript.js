// @ts-nocheck
PropertiesService.getScriptProperties();

var TOKEN_URL = "https://train06.netforument.com/nftstrain2017/xWeb/JSON/Authenticate";
var POST_URL = "https://train06.netforument.com/nftstrain2017/xWeb/webhook/IncomingWebhookSamplex";

function getxWebToken() {
    var authPayload = {
        "Authenticate": {
            "userName": "xweb_test",
            "password": "xWeb@2024"
        }
    };

    var optionsToken = {
        "method": "POST",
        "contentType": "application/json",
        "payload": JSON.stringify(authPayload)
    }

    var tokenResponse = UrlFetchApp.fetch(TOKEN_URL, optionsToken);
    var token = JSON.parse(tokenResponse.getContentText()).Token;
    console.log(`Get token successful: ${token}`);
    return token;
}

function getTimeValue(ts) {
    const d = new Date(ts);
    return d.toISOString();
}


function getEvent(formResponse) {
    var properties = PropertiesService.getScriptProperties();
    const Event = {
        Id: properties.getProperty("EventID")
    };

    return Event;
}

function getSessionData(formResponse) {
    var SessionsData = [];
    for (const itemResponse of formResponse.getItemResponses()) {
        if (itemResponse.getItem().getIndex() == 3) {

            var ses = [];
            ses = itemResponse.getResponse();
            console.log(ses);

            for (let i = 0; i < ses.length; i++) {
                const Session = {
                    Title: ses[i],
                    Status: "register"
                };
                SessionsData.push(Session)
            }

        }
    }
    return SessionsData;

}



function getIndividualData(formResponse) {
    const Individual = {
        FirstName: "",
        LastName: "",
        EmailAddress: ""
    };
    for (const itemResponse of formResponse.getItemResponses()) {

        for (const itemResponse of formResponse.getItemResponses()) {

            var itemIndex = itemResponse.getItem().getIndex();
            switch (itemIndex) {
                case 0:
                    Individual.FirstName = itemResponse.getResponse();
                    break;
                case 1:
                    Individual.LastName = itemResponse.getResponse();
                    break;
                case 2:
                    Individual.EmailAddress = itemResponse.getResponse();
                    break;
                default:
                    "";
            }

        }
    }
    return Individual
}

function getJsonResponse(formResponse) {
    var json = {
        "id": formResponse.getId(),
        "email": formResponse.getRespondentEmail(),
        "ts": getTimeValue(formResponse.getTimestamp())
    }

    json.Individual = getIndividualData(formResponse);
    json.Event = getEvent(formResponse);
    json.Sessions = getSessionData(formResponse);
    return JSON.stringify(json);
}


function onSubmit(e) {

    try {
        var token = getxWebToken();
        const formResponse = (e.response);
        const jsonResponse = getJsonResponse(formResponse);
        console.log(jsonResponse);

        var options = {
            "method": "POST",
            "headers": { "Authorization": `Bearer ${token}` },
            "contentType": "application/json",
            "payload": jsonResponse
        };

        var response = UrlFetchApp.fetch(POST_URL, options);
        console.log("Post Response: " + response.getContentText());
    }
    catch (ex) {
        console.error(`Error: ${ex}`);
    }
}
