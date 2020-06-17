﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Any_Inputfield : MonoBehaviour
{
    private Any_Colours anyColors = new Any_Colours();

    public void setError() {
        gameObject.GetComponent<Image>().color = anyColors.get_Colour_Red();
    }

    public void setNormal() {
        if (gameObject.GetComponent<Image>().color != anyColors.get_Colour_Normal()) {
            gameObject.GetComponent<Image>().color = anyColors.get_Colour_Normal();
        }

        // If it is the Register - Username inputfield
        if (gameObject.name.ToString() == "InputField_Username") { //xxxxxxxxx
            GameObject.Find("Holder_Check_Result").gameObject.transform.Find("Text").gameObject.GetComponent<Text>().text = "";
        }
    }
}
